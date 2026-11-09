using System.Security.Claims;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Igevo.Vernetzt.Core.DTOs.CardDav;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Server.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Igevo.Vernetzt.Server.Controllers;

/// <summary>
/// Read-only CardDAV server (RFC 6352 subset on top of WebDAV, RFC 4918) with one address book per user.
/// Supports discovery (current-user-principal, addressbook-home-set), PROPFIND with Depth 0/1, the
/// REPORTs addressbook-multiget and addressbook-query, and GET of single vCards. Change detection
/// happens via getctag/getetag; sync-collection is not offered, so clients fall back to that.
/// Writing methods are rejected with 403.
/// </summary>
[ApiController]
[Route("carddav")]
[Authorize(AuthenticationSchemes = CardDavBasicAuthenticationHandler.SchemeName)]
[EnableRateLimiting(RateLimitPolicyName)]
[ApiExplorerSettings(IgnoreApi = true)]
public class CardDavController : ControllerBase
{
    public const string RateLimitPolicyName = "CardDav";
    internal const string AddressBookSegment = "vernetzt";
    private const int MaxRequestBodyBytes = 64 * 1024;

    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace Card = "urn:ietf:params:xml:ns:carddav";
    private static readonly XNamespace CalendarServer = "http://calendarserver.org/ns/";

    private readonly ICardDavService _cardDavService;
    private readonly ILogger<CardDavController> _logger;

    public CardDavController(ICardDavService cardDavService, ILogger<CardDavController> logger)
    {
        _cardDavService = cardDavService;
        _logger = logger;
    }

    /// <summary>
    /// Service discovery (RFC 6764): clients that only know the host name land here.
    /// </summary>
    [AcceptVerbs("GET", "HEAD", "PROPFIND", Route = "/.well-known/carddav")]
    [AllowAnonymous]
    public IActionResult WellKnown()
    {
        // 301 as described in RFC 6764; CardDAV clients repeat the PROPFIND at the new location.
        return LocalRedirectPermanent($"{Request.PathBase}/carddav/");
    }

    [AcceptVerbs("OPTIONS", Route = "{**path}")]
    [AllowAnonymous]
    public IActionResult Options()
    {
        AddDavHeaders();
        return Ok();
    }

    [AcceptVerbs("PROPFIND", Route = "{**path}")]
    public async Task<IActionResult> Propfind(string? path, CancellationToken cancellationToken)
    {
        AddDavHeaders();
        var userId = GetUserId();
        var resource = ParsePath(path);
        if (resource.Kind == ResourceKind.Unknown) return NotFound();
        if (resource.UserId is { } owner && owner != userId) return StatusCode(StatusCodes.Status403Forbidden);

        var (body, error) = await ReadXmlBodyAsync(cancellationToken);
        if (error is not null) return error;

        var request = PropfindRequest.Parse(body);
        var depth = ParseDepth();

        var responses = new List<XElement>();
        switch (resource.Kind)
        {
            case ResourceKind.Root:
                responses.Add(BuildResponse(RootHref(), request, RootProperties(userId)));
                break;

            case ResourceKind.Principal:
                responses.Add(BuildResponse(PrincipalHref(userId), request, PrincipalProperties(userId)));
                break;

            case ResourceKind.Home:
                responses.Add(BuildResponse(HomeHref(userId), request, HomeProperties(userId)));
                if (depth > 0)
                {
                    var book = await _cardDavService.GetAddressBookAsync(userId, cancellationToken);
                    responses.Add(BuildResponse(AddressBookHref(userId), request, AddressBookProperties(userId, book)));
                }
                break;

            case ResourceKind.AddressBook:
            {
                var book = await _cardDavService.GetAddressBookAsync(userId, cancellationToken);
                responses.Add(BuildResponse(AddressBookHref(userId), request, AddressBookProperties(userId, book)));
                if (depth > 0)
                {
                    responses.AddRange(book.Cards.Select(card =>
                        BuildResponse(CardHref(userId, card), request, CardProperties(userId, card, includeData: false))));
                }
                break;
            }

            case ResourceKind.Card:
            {
                var book = await _cardDavService.GetAddressBookAsync(userId, cancellationToken);
                var card = book.Find(resource.CardName!);
                if (card is null) return NotFound();
                responses.Add(BuildResponse(CardHref(userId, card), request, CardProperties(userId, card, includeData: false)));
                break;
            }
        }

        return MultiStatus(responses);
    }

    [AcceptVerbs("REPORT", Route = "{**path}")]
    public async Task<IActionResult> Report(string? path, CancellationToken cancellationToken)
    {
        AddDavHeaders();
        var userId = GetUserId();
        var resource = ParsePath(path);
        if (resource.Kind == ResourceKind.Unknown) return NotFound();
        if (resource.UserId is { } owner && owner != userId) return StatusCode(StatusCodes.Status403Forbidden);
        if (resource.Kind is not (ResourceKind.AddressBook or ResourceKind.Card)) return UnsupportedReport();

        var (body, error) = await ReadXmlBodyAsync(cancellationToken);
        if (error is not null) return error;
        if (body?.Root is not { } root) return BadRequest();

        var request = PropfindRequest.Parse(body);
        var book = await _cardDavService.GetAddressBookAsync(userId, cancellationToken);
        var responses = new List<XElement>();

        if (root.Name == Card + "addressbook-multiget")
        {
            foreach (var href in root.Elements(Dav + "href").Select(h => h.Value.Trim()).Distinct())
            {
                var card = FindCardByHref(book, userId, href);
                responses.Add(card is null
                    ? new XElement(Dav + "response",
                        new XElement(Dav + "href", href),
                        new XElement(Dav + "status", "HTTP/1.1 404 Not Found"))
                    : BuildResponse(CardHref(userId, card), request, CardProperties(userId, card, includeData: true)));
            }
        }
        else if (root.Name == Card + "addressbook-query")
        {
            // Filters are not evaluated: the address book is small and clients filter locally.
            IEnumerable<CardDavCard> cards = resource.Kind == ResourceKind.Card
                ? book.Find(resource.CardName!) is { } single ? [single] : []
                : book.Cards;
            responses.AddRange(cards.Select(card =>
                BuildResponse(CardHref(userId, card), request, CardProperties(userId, card, includeData: true))));
        }
        else
        {
            return UnsupportedReport();
        }

        return MultiStatus(responses);
    }

    [AcceptVerbs("GET", "HEAD", Route = "{**path}")]
    public async Task<IActionResult> Get(string? path, CancellationToken cancellationToken)
    {
        AddDavHeaders();
        var userId = GetUserId();
        var resource = ParsePath(path);
        if (resource.Kind == ResourceKind.Unknown) return NotFound();
        if (resource.UserId is { } owner && owner != userId) return StatusCode(StatusCodes.Status403Forbidden);

        var book = await _cardDavService.GetAddressBookAsync(userId, cancellationToken);
        switch (resource.Kind)
        {
            case ResourceKind.Card:
            {
                var card = book.Find(resource.CardName!);
                if (card is null) return NotFound();

                Response.Headers.ETag = card.ETag;
                var ifNoneMatch = Request.Headers.IfNoneMatch
                    .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                if (ifNoneMatch.Any(v => v == card.ETag || v == $"W/{card.ETag}" || v == "*"))
                {
                    return StatusCode(StatusCodes.Status304NotModified);
                }

                return Content(card.Content, "text/vcard; charset=utf-8", Encoding.UTF8);
            }

            case ResourceKind.AddressBook:
                // The whole address book as one .vcf file, e.g. for a one-time import.
                Response.Headers.ETag = $"\"{book.CTag}\"";
                Response.Headers.ContentDisposition = "inline; filename=\"vernetzt.vcf\"";
                return Content(string.Concat(book.Cards.Select(c => c.Content)), "text/vcard; charset=utf-8", Encoding.UTF8);

            default:
                return Content("Vernetzt CardDAV", "text/plain; charset=utf-8", Encoding.UTF8);
        }
    }

    [AcceptVerbs("PUT", "DELETE", "POST", "PATCH", "PROPPATCH", "MKCOL", "MKCALENDAR", "COPY", "MOVE", "LOCK", "UNLOCK", "ACL",
        Route = "{**path}")]
    public IActionResult ReadOnly()
    {
        AddDavHeaders();
        var error = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Dav + "error",
                new XAttribute(XNamespace.Xmlns + "d", Dav.NamespaceName),
                new XElement(Dav + "need-privileges")));
        return new ContentResult
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentType = "application/xml; charset=utf-8",
            Content = error.ToString(SaveOptions.DisableFormatting)
        };
    }

    // ------------------------------------------------------------ resources and properties

    private enum ResourceKind { Unknown, Root, Principal, Home, AddressBook, Card }

    private sealed record Resource(ResourceKind Kind, Guid? UserId = null, string? CardName = null);

    private static Resource ParsePath(string? path)
    {
        var segments = (path ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return new Resource(ResourceKind.Root);

        if (segments.Length < 2 || !Guid.TryParse(segments[1], out var userId))
        {
            return new Resource(ResourceKind.Unknown);
        }

        return segments[0] switch
        {
            "principals" when segments.Length == 2 => new Resource(ResourceKind.Principal, userId),
            "addressbooks" when segments.Length == 2 => new Resource(ResourceKind.Home, userId),
            "addressbooks" when segments.Length == 3 && segments[2] == AddressBookSegment =>
                new Resource(ResourceKind.AddressBook, userId),
            "addressbooks" when segments.Length == 4 && segments[2] == AddressBookSegment =>
                new Resource(ResourceKind.Card, userId, Uri.UnescapeDataString(segments[3])),
            _ => new Resource(ResourceKind.Unknown)
        };
    }

    private string RootHref() => $"{Request.PathBase}/carddav/";
    private string PrincipalHref(Guid userId) => $"{Request.PathBase}/carddav/principals/{userId}/";
    private string HomeHref(Guid userId) => $"{Request.PathBase}/carddav/addressbooks/{userId}/";
    private string AddressBookHref(Guid userId) => $"{HomeHref(userId)}{AddressBookSegment}/";
    private string CardHref(Guid userId, CardDavCard card) => $"{AddressBookHref(userId)}{Uri.EscapeDataString(card.ResourceName)}";

    private CardDavCard? FindCardByHref(CardDavAddressBook book, Guid userId, string href)
    {
        var path = Uri.TryCreate(href, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : href;
        path = Uri.UnescapeDataString(path);
        var prefix = Uri.UnescapeDataString(AddressBookHref(userId));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return book.Find(path[prefix.Length..]);
    }

    private Dictionary<XName, object?> CommonProperties(Guid userId) => new()
    {
        [Dav + "current-user-principal"] = new XElement(Dav + "href", PrincipalHref(userId)),
        [Dav + "current-user-privilege-set"] = ReadPrivileges(),
        [Dav + "owner"] = new XElement(Dav + "href", PrincipalHref(userId))
    };

    private Dictionary<XName, object?> RootProperties(Guid userId)
    {
        var props = CommonProperties(userId);
        props[Dav + "resourcetype"] = new XElement(Dav + "collection");
        props[Dav + "displayname"] = "Vernetzt";
        props[Dav + "principal-URL"] = new XElement(Dav + "href", PrincipalHref(userId));
        props[Card + "addressbook-home-set"] = new XElement(Dav + "href", HomeHref(userId));
        return props;
    }

    private Dictionary<XName, object?> PrincipalProperties(Guid userId)
    {
        var props = CommonProperties(userId);
        props[Dav + "resourcetype"] = new object[] { new XElement(Dav + "collection"), new XElement(Dav + "principal") };
        props[Dav + "displayname"] = "Vernetzt";
        props[Dav + "principal-URL"] = new XElement(Dav + "href", PrincipalHref(userId));
        props[Card + "addressbook-home-set"] = new XElement(Dav + "href", HomeHref(userId));
        props[Dav + "principal-collection-set"] = new XElement(Dav + "href", $"{Request.PathBase}/carddav/principals/");
        return props;
    }

    private Dictionary<XName, object?> HomeProperties(Guid userId)
    {
        var props = CommonProperties(userId);
        props[Dav + "resourcetype"] = new XElement(Dav + "collection");
        props[Dav + "displayname"] = "Adressbücher";
        return props;
    }

    private Dictionary<XName, object?> AddressBookProperties(Guid userId, CardDavAddressBook book)
    {
        var props = CommonProperties(userId);
        props[Dav + "resourcetype"] = new object[] { new XElement(Dav + "collection"), new XElement(Card + "addressbook") };
        props[Dav + "displayname"] = "Vernetzt";
        props[Card + "addressbook-description"] = "Kontakte aus Vernetzt (nur lesend)";
        props[Card + "supported-address-data"] = new XElement(Card + "address-data-type",
            new XAttribute("content-type", "text/vcard"), new XAttribute("version", "3.0"));
        props[Card + "max-resource-size"] = "1048576";
        props[CalendarServer + "getctag"] = book.CTag;
        props[Dav + "getetag"] = $"\"{book.CTag}\"";
        props[Dav + "supported-report-set"] = new object[]
        {
            new XElement(Dav + "supported-report", new XElement(Dav + "report", new XElement(Card + "addressbook-multiget"))),
            new XElement(Dav + "supported-report", new XElement(Dav + "report", new XElement(Card + "addressbook-query")))
        };
        return props;
    }

    private Dictionary<XName, object?> CardProperties(Guid userId, CardDavCard card, bool includeData)
    {
        var props = CommonProperties(userId);
        props[Dav + "resourcetype"] = null;
        props[Dav + "getetag"] = card.ETag;
        props[Dav + "getcontenttype"] = "text/vcard; charset=utf-8";
        props[Dav + "getcontentlength"] = Encoding.UTF8.GetByteCount(card.Content).ToString();
        // address-data is only part of allprop for REPORTs (it can be large).
        props[Card + "address-data"] = includeData ? card.Content : PropfindRequest.OnlyIfRequested(card.Content);
        return props;
    }

    private static XElement ReadPrivileges() =>
        new(Dav + "privilege", new XElement(Dav + "read"));

    // ------------------------------------------------------------ multistatus building

    private static XElement BuildResponse(string href, PropfindRequest request, Dictionary<XName, object?> available)
    {
        var found = new List<XElement>();
        var missing = new List<XElement>();

        IEnumerable<XName> names = request.Mode switch
        {
            PropfindMode.Prop => request.Properties,
            _ => available.Where(p => p.Value is not PropfindRequest.RequestedOnly).Select(p => p.Key)
        };

        foreach (var name in names)
        {
            if (!available.TryGetValue(name, out var value))
            {
                missing.Add(new XElement(name));
                continue;
            }

            if (value is PropfindRequest.RequestedOnly requestedOnly) value = requestedOnly.Value;
            found.Add(request.Mode == PropfindMode.PropName ? new XElement(name) : new XElement(name, value));
        }

        var response = new XElement(Dav + "response", new XElement(Dav + "href", href));
        if (found.Count > 0 || missing.Count == 0)
        {
            response.Add(new XElement(Dav + "propstat",
                new XElement(Dav + "prop", found),
                new XElement(Dav + "status", "HTTP/1.1 200 OK")));
        }

        if (missing.Count > 0)
        {
            response.Add(new XElement(Dav + "propstat",
                new XElement(Dav + "prop", missing),
                new XElement(Dav + "status", "HTTP/1.1 404 Not Found")));
        }

        return response;
    }

    private static ContentResult MultiStatus(IEnumerable<XElement> responses)
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Dav + "multistatus",
                new XAttribute(XNamespace.Xmlns + "d", Dav.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "card", Card.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "cs", CalendarServer.NamespaceName),
                responses));

        return new ContentResult
        {
            StatusCode = StatusCodes.Status207MultiStatus,
            ContentType = "application/xml; charset=utf-8",
            Content = document.Declaration + document.ToString(SaveOptions.DisableFormatting)
        };
    }

    private ContentResult UnsupportedReport()
    {
        var error = new XDocument(
            new XElement(Dav + "error",
                new XAttribute(XNamespace.Xmlns + "d", Dav.NamespaceName),
                new XElement(Dav + "supported-report")));
        return new ContentResult
        {
            StatusCode = StatusCodes.Status403Forbidden,
            ContentType = "application/xml; charset=utf-8",
            Content = error.ToString(SaveOptions.DisableFormatting)
        };
    }

    // ------------------------------------------------------------ request helpers

    private enum PropfindMode { AllProp, PropName, Prop }

    private sealed class PropfindRequest
    {
        public PropfindMode Mode { get; private init; } = PropfindMode.AllProp;
        public IReadOnlyList<XName> Properties { get; private init; } = [];

        /// <summary>Marks a property that is not part of allprop, only returned when asked for by name.</summary>
        public sealed record RequestedOnly(object? Value);

        public static RequestedOnly OnlyIfRequested(object? value) => new(value);

        /// <summary>Reads &lt;prop&gt;, &lt;allprop&gt; or &lt;propname&gt; from a PROPFIND or REPORT body.</summary>
        public static PropfindRequest Parse(XDocument? body)
        {
            var root = body?.Root;
            if (root is null) return new PropfindRequest();

            if (root.Element(Dav + "propname") is not null) return new PropfindRequest { Mode = PropfindMode.PropName };

            var prop = root.Element(Dav + "prop");
            if (prop is null) return new PropfindRequest();

            return new PropfindRequest
            {
                Mode = PropfindMode.Prop,
                Properties = prop.Elements().Select(e => e.Name).Distinct().ToList()
            };
        }
    }

    private int ParseDepth()
    {
        var depth = Request.Headers["Depth"].ToString().Trim();
        // Depth "infinity" (the default) is answered like 1: the tree is never deeper.
        return depth == "0" ? 0 : 1;
    }

    private async Task<(XDocument? Body, IActionResult? Error)> ReadXmlBodyAsync(CancellationToken cancellationToken)
    {
        if (Request.ContentLength is > MaxRequestBodyBytes)
        {
            return (null, StatusCode(StatusCodes.Status413PayloadTooLarge));
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxRequestBodyBytes)
            {
                return (null, StatusCode(StatusCodes.Status413PayloadTooLarge));
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0) return (null, null);

        buffer.Position = 0;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxRequestBodyBytes,
            Async = true
        };

        try
        {
            using var reader = XmlReader.Create(buffer, settings);
            return (await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken), null);
        }
        catch (XmlException ex)
        {
            _logger.LogDebug(ex, "Invalid CardDAV request body");
            return (null, BadRequest());
        }
    }

    private void AddDavHeaders()
    {
        Response.Headers["DAV"] = "1, 3, addressbook";
        Response.Headers.Allow = "OPTIONS, GET, HEAD, PROPFIND, REPORT";
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("Missing user id claim.");
    }
}
