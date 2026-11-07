using Ganss.Xss;

namespace Igevo.Vernetzt.Server.Security;

public static class EmailHtmlSanitizerFactory
{
    public static IHtmlSanitizer Create()
    {
        var options = new HtmlSanitizerOptions();
        options.AllowedTags.Clear();
        options.AllowedAttributes.Clear();
        options.AllowedSchemes.Clear();
        options.UriAttributes.Clear();

        foreach (var tag in new[] { "p", "br", "strong", "b", "em", "i", "u", "s", "h1", "h2", "h3", "ul", "ol", "li", "blockquote", "a" })
            options.AllowedTags.Add(tag);
        foreach (var attribute in new[] { "href", "title" })
            options.AllowedAttributes.Add(attribute);
        options.UriAttributes.Add("href");
        foreach (var scheme in new[] { "http", "https", "mailto" })
            options.AllowedSchemes.Add(scheme);

        return new HtmlSanitizer(options);
    }
}
