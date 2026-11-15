using Igevo.Vernetzt.Core.Configuration;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Core.Interfaces.Services;
using Igevo.Vernetzt.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Igevo.Vernetzt.Infrastructure.Services;

/// <summary>
/// Service for initializing the database on first startup.
/// Applies pending migrations and creates seed data if needed.
/// </summary>
public class DatabaseInitializationService
{
    private readonly VernetztDbContext _context;
    private readonly IOptions<AppSettings> _appSettings;
    private readonly IDatabaseBackupService _backupService;
    private readonly IOptions<BackupOptions> _backupOptions;
    private readonly ILogger<DatabaseInitializationService> _logger;

    public DatabaseInitializationService(
        VernetztDbContext context,
        IOptions<AppSettings> appSettings,
        IDatabaseBackupService backupService,
        IOptions<BackupOptions> backupOptions,
        ILogger<DatabaseInitializationService> logger)
    {
        _context = context;
        _appSettings = appSettings;
        _backupService = backupService;
        _backupOptions = backupOptions;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the database on startup.
    /// Applies pending migrations and creates initial data if needed.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Apply pending migrations
            await ApplyMigrationsAsync(cancellationToken);

            // Create initial admin user if database is empty
            await CreateInitialAdminAsync(cancellationToken);

            // Seed app settings from appsettings.json if not yet in database
            await SeedAppSettingsAsync(cancellationToken);

            // Seed development data if enabled
            if (_appSettings.Value.SeedDevelopmentData)
            {
                await SeedDevelopmentDataAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }

    private async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        // WAL lets readers continue while one connection writes; the mode is stored in the database file.
        await _context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);

        var pendingMigrations = (await _context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pendingMigrations.Count == 0)
        {
            _logger.LogInformation("Database schema is up to date");
            return;
        }

        var appliedMigrations = await _context.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (_backupOptions.Value.BeforeMigration && appliedMigrations.Any())
        {
            // An existing database is about to change: keep a copy to return to if the update goes wrong.
            _logger.LogInformation("Creating backup before applying {Count} migration(s)", pendingMigrations.Count);
            await _backupService.CreateBackupAsync(BackupTrigger.PreMigration, cancellationToken);
        }

        _logger.LogInformation("Applying {Count} pending migration(s)...", pendingMigrations.Count);
        await _context.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Database migrations applied successfully");
    }

    private async Task CreateInitialAdminAsync(CancellationToken cancellationToken)
    {
        var adminEmail = _appSettings.Value.SystemAdminEmail;
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            _logger.LogWarning("SystemAdminEmail not configured. Skipping initial user creation.");
            return;
        }

        // Check if an admin user with this email already exists
        var existingAdmin = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == adminEmail, cancellationToken);

        if (existingAdmin != null)
        {
            // Ensure the existing user has the SystemAdmin claim
            if (!existingAdmin.Claims.Contains(UserClaim.SystemAdmin))
            {
                _logger.LogWarning("User with SystemAdminEmail {Email} exists but lacks SystemAdmin claim. Adding claim.", EmailAddressHelper.Mask(adminEmail));
                existingAdmin.Claims.Add(UserClaim.SystemAdmin);
                existingAdmin.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }
            else
            {
                _logger.LogInformation("System administrator user already exists: {Email}", EmailAddressHelper.Mask(adminEmail));
            }
            return;
        }

        _logger.LogInformation("Creating initial system administrator user: {Email}", EmailAddressHelper.Mask(adminEmail));

        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "System",
            LastName = "Administrator",
            Email = adminEmail,
            EmailVerified = true,
            Claims = [UserClaim.SystemAdmin],
            EmailVisibility = VisibilityLevel.AllUsers,
            PhoneVisibility = VisibilityLevel.AllUsers,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _context.Users.AddAsync(adminUser, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Initial system administrator created successfully with ID: {UserId}",
            adminUser.Id);
    }

    private async Task SeedDevelopmentDataAsync(CancellationToken cancellationToken)
    {
        // Only seed if no clubs exist yet (excluding admin user check)
        if (await _context.Clubs.AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Development data already exists - skipping seeding");
            return;
        }

        _logger.LogInformation("Seeding development data...");

        var now = DateTime.UtcNow;
        var today = now.Date;

        var clubTypes = new List<ClubType>
        {
            new() { Id = Guid.NewGuid(), Name = "Sportverein", Description = "Allgemeiner Mehrsparten-Sportverein" },
            new() { Id = Guid.NewGuid(), Name = "Fußballverein", Description = "Fußball und verwandte Sportarten" },
            new() { Id = Guid.NewGuid(), Name = "Tennisverein", Description = "Tennis und Racketsport" },
            new() { Id = Guid.NewGuid(), Name = "Schwimmverein", Description = "Schwimm- und Wassersport" },
            new() { Id = Guid.NewGuid(), Name = "Turnverein", Description = "Turnen, Gymnastik und Breitensport" },
            new() { Id = Guid.NewGuid(), Name = "Radsportverein", Description = "Radsport und Radtouristik" },
            new() { Id = Guid.NewGuid(), Name = "Kulturverein", Description = "Kultur, Musik und gemeinschaftliches Engagement" }
        };
        await _context.ClubTypes.AddRangeAsync(clubTypes, cancellationToken);

        var positions = new List<ContactPosition>
        {
            new() { Id = Guid.NewGuid(), Name = "1. Vorsitzende/r" },
            new() { Id = Guid.NewGuid(), Name = "2. Vorsitzende/r" },
            new() { Id = Guid.NewGuid(), Name = "Kassierer/in" },
            new() { Id = Guid.NewGuid(), Name = "Schriftführer/in" },
            new() { Id = Guid.NewGuid(), Name = "Jugendwart/in" },
            new() { Id = Guid.NewGuid(), Name = "Pressewart/in" },
            new() { Id = Guid.NewGuid(), Name = "Trainer/in" },
            new() { Id = Guid.NewGuid(), Name = "Abteilungsleiter/in" },
            new() { Id = Guid.NewGuid(), Name = "Geschäftsstelle" },
            new() { Id = Guid.NewGuid(), Name = "Veranstaltungskoordination" }
        };
        await _context.ContactPositions.AddRangeAsync(positions, cancellationToken);

        User CreateUser(
            string firstName,
            string lastName,
            string email,
            string? phoneNumber = null,
            List<UserClaim>? claims = null,
            VisibilityLevel emailVisibility = VisibilityLevel.AllUsers,
            VisibilityLevel phoneVisibility = VisibilityLevel.AllUsers) => new()
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PhoneNumber = phoneNumber,
            EmailVerified = true,
            Claims = claims ?? [],
            EmailVisibility = emailVisibility,
            PhoneVisibility = phoneVisibility,
            LastLoginAt = now.AddDays(-7)
        };

        // The email addresses make each authorization scenario immediately recognizable on the login page.
        var manager = CreateUser("Maren", "Management", "manager@test.de", "+49 234 555-0101", [UserClaim.Manager]);
        var dataReader = CreateUser("Lena", "Leser", "leser@test.de", "+49 234 555-0102", [UserClaim.DataReader]);
        var clubEditor = CreateUser("Vera", "Vereinsschreiber", "verein.schreiben@test.de", "+49 234 555-0103");
        var clubReader = CreateUser("Ralf", "Vereinsleser", "verein.lesen@test.de", "+49 234 555-0104");
        var departmentEditor = CreateUser("Daria", "Abteilungsschreiber", "abteilung.schreiben@test.de", "+49 234 555-0105");
        var departmentReader = CreateUser("Anton", "Abteilungsleser", "abteilung.lesen@test.de", "+49 234 555-0106");
        var groupEditor = CreateUser("Greta", "Gruppenschreiber", "gruppe.schreiben@test.de", "+49 234 555-0108");
        var multiClubEditor = CreateUser("Mila", "Mehrfachzuständig", "mehrere.vereine@test.de", "+49 234 555-0107");
        var unassignedUser = CreateUser("Uwe", "Ohne Zuordnung", "ohne.zuordnung@test.de", null, emailVisibility: VisibilityLevel.Internal);
        var maria = CreateUser("Maria", "Müller", "maria.mueller@example.com", "+49 234 555-0110", emailVisibility: VisibilityLevel.Public, phoneVisibility: VisibilityLevel.Public);
        var thomas = CreateUser("Thomas", "Schmidt", "thomas.schmidt@example.com", "+49 234 555-0111", phoneVisibility: VisibilityLevel.Internal);
        var anna = CreateUser("Anna", "Weber", "anna.weber@example.com", "+49 234 555-0112");
        var cem = CreateUser("Cem", "Yilmaz", "cem.yilmaz@example.com", "+49 234 555-0113", emailVisibility: VisibilityLevel.Public);
        var sophie = CreateUser("Sophie", "Klein", "sophie.klein@example.com", "+49 234 555-0114");
        var jonas = CreateUser("Jonas", "Berg", "jonas.berg@example.com", "+49 234 555-0115");

        var users = new[]
        {
            manager, dataReader, clubEditor, clubReader, departmentEditor, departmentReader, groupEditor,
            multiClubEditor, unassignedUser, maria, thomas, anna, cem, sophie, jonas
        };
        await _context.Users.AddRangeAsync(users, cancellationToken);

        // Demo contact persons released publicly per organization (each assignment carries its own visibility)
        var publicContactOrganizations = new HashSet<Guid>();

        Club CreateClub(
            string name,
            int typeIndex,
            string street,
            string postalCode,
            string city,
            string email,
            string websiteUrl,
            VisibilityLevel visibility = VisibilityLevel.AllUsers)
        {
            var club = new Club
            {
                Id = Guid.NewGuid(),
                Name = name,
                ClubTypeId = clubTypes[typeIndex].Id,
                Street = street,
                PostalCode = postalCode,
                City = city,
                Email = email,
                WebsiteUrl = websiteUrl
            };
            if (visibility == VisibilityLevel.Public) publicContactOrganizations.Add(club.Id);
            return club;
        }

        var tsv = CreateClub("TSV Musterstadt 1900", 0, "Sportplatzweg 1", "44787", "Bochum", "info@tsv-musterstadt.example", "https://tsv-musterstadt.example", VisibilityLevel.Public);
        var fc = CreateClub("FC Beispielberg 09", 1, "Am Stadion 22", "44791", "Bochum", "kontakt@fc-beispielberg.example", "https://fc-beispielberg.example", VisibilityLevel.Public);
        var tennis = CreateClub("Tennisfreunde Stadtpark", 2, "Parkallee 7", "44789", "Bochum", "vorstand@tennisfreunde-stadtpark.example", "https://tennisfreunde-stadtpark.example");
        var swimming = CreateClub("Schwimmclub Blau-Weiß", 3, "Wellenweg 4", "44801", "Bochum", "mail@sc-blau-weiss.example", "https://sc-blau-weiss.example", VisibilityLevel.Public);
        var gymnastics = CreateClub("Turngemeinschaft Süd 1925", 4, "Hallenstraße 18", "44799", "Bochum", "geschaeftsstelle@tg-sued.example", "https://tg-sued.example");
        var cycling = CreateClub("Radsportfreunde Ruhrtal", 5, "Ruhrtalweg 31", "44879", "Bochum", "kontakt@rsf-ruhrtal.example", "https://rsf-ruhrtal.example");
        var culture = CreateClub("Kulturtreff Vielfalt", 6, "Marktstraße 12", "44787", "Bochum", "hallo@kulturtreff-vielfalt.example", "https://kulturtreff-vielfalt.example", VisibilityLevel.Public);
        tsv.SocialMediaLinks =
        [
            new SocialMediaLink(SocialMediaPlatform.Instagram, "https://www.instagram.com/tsv.musterstadt.example"),
            new SocialMediaLink(SocialMediaPlatform.Facebook, "https://www.facebook.com/tsv.musterstadt.example"),
            new SocialMediaLink(SocialMediaPlatform.YouTube, "https://www.youtube.com/@tsv-musterstadt-example")
        ];
        fc.SocialMediaLinks = [new SocialMediaLink(SocialMediaPlatform.Instagram, "https://www.instagram.com/fc.beispielberg.example")];
        swimming.SocialMediaLinks = [new SocialMediaLink(SocialMediaPlatform.Facebook, "https://www.facebook.com/sc.blau.weiss.example")];
        culture.SocialMediaLinks =
        [
            new SocialMediaLink(SocialMediaPlatform.Mastodon, "https://mastodon.example/@kulturtreff"),
            new SocialMediaLink(SocialMediaPlatform.WhatsApp, "https://whatsapp.com/channel/kulturtreff-example")
        ];
        var clubs = new[] { tsv, fc, tennis, swimming, gymnastics, cycling, culture };
        await _context.Clubs.AddRangeAsync(clubs, cancellationToken);

        Department CreateDepartment(
            Club club,
            string name,
            string? email = null,
            VisibilityLevel visibility = VisibilityLevel.AllUsers)
        {
            var department = new Department
            {
                Id = Guid.NewGuid(),
                ClubId = club.Id,
                Name = name,
                Email = email
            };
            if (visibility == VisibilityLevel.Public) publicContactOrganizations.Add(department.Id);
            return department;
        }

        var tsvYouth = CreateDepartment(tsv, "Jugendabteilung", "jugend@tsv-musterstadt.example", VisibilityLevel.Public);
        var tsvRunning = CreateDepartment(tsv, "Leichtathletik & Lauftreff", "laufen@tsv-musterstadt.example");
        var tsvTableTennis = CreateDepartment(tsv, "Tischtennis", "tischtennis@tsv-musterstadt.example");
        var tsvHandball = CreateDepartment(tsv, "Handball", "handball@tsv-musterstadt.example", VisibilityLevel.Public);
        tsvHandball.Description = "Handball für alle Altersklassen – von den Bambini bis zu den Alten Herren. Wir freuen uns über neue Spielerinnen und Spieler!";

        // Groups (one level below a department) share the club and inherit edit rights from the department.
        Department CreateGroup(Department parent, string name, string description, string? email = null)
        {
            var group = new Department
            {
                Id = Guid.NewGuid(),
                ClubId = parent.ClubId,
                ParentDepartmentId = parent.Id,
                Name = name,
                Description = description,
                Email = email
            };
            publicContactOrganizations.Add(group.Id);
            return group;
        }

        var handballBambini = CreateGroup(tsvHandball, "Bambini",
            "Für Kinder von 4 bis 6 Jahren.\nTraining: freitags 16:00–17:00 Uhr, Sporthalle Nord.\nSchnuppern ist jederzeit möglich.",
            "bambini@tsv-musterstadt.example");
        var handballFirst = CreateGroup(tsvHandball, "1. Mannschaft",
            "Bezirksliga Herren.\nTraining: dienstags und donnerstags 19:30–21:30 Uhr, Sporthalle Nord.");
        var handballVeterans = CreateGroup(tsvHandball, "Alte Herren",
            "Freizeithandball ab 35.\nTraining: mittwochs 20:00–21:30 Uhr, Sporthalle Süd.");
        handballFirst.SocialMediaLinks = [new SocialMediaLink(SocialMediaPlatform.Instagram, "https://www.instagram.com/tsv.handball.erste.example")];
        var fcYouth = CreateDepartment(fc, "Junioren", "junioren@fc-beispielberg.example", VisibilityLevel.Public);
        var fcWomen = CreateDepartment(fc, "Frauenfußball", "frauen@fc-beispielberg.example", VisibilityLevel.Public);
        var tennisYouth = CreateDepartment(tennis, "Jugendtraining", "jugend@tennisfreunde-stadtpark.example");
        var swimmingCompetitive = CreateDepartment(swimming, "Wettkampfmannschaft", "wettkampf@sc-blau-weiss.example");
        var swimmingCourses = CreateDepartment(swimming, "Schwimmkurse", "kurse@sc-blau-weiss.example", VisibilityLevel.Public);
        var gymnasticsChildren = CreateDepartment(gymnastics, "Kinderturnen", "kinder@tg-sued.example");
        var gymnasticsFitness = CreateDepartment(gymnastics, "Fitness & Gesundheit", "fitness@tg-sued.example");
        var cyclingTouring = CreateDepartment(cycling, "Radtouristik", "touren@rsf-ruhrtal.example");
        var cultureMusic = CreateDepartment(culture, "Musik & Chor", "musik@kulturtreff-vielfalt.example", VisibilityLevel.Public);
        var cultureEvents = CreateDepartment(culture, "Veranstaltungen", "veranstaltungen@kulturtreff-vielfalt.example", VisibilityLevel.Public);
        tsvYouth.SocialMediaLinks =
        [
            new SocialMediaLink(SocialMediaPlatform.Instagram, "https://www.instagram.com/tsv.jugend.example"),
            new SocialMediaLink(SocialMediaPlatform.TikTok, "https://www.tiktok.com/@tsv.jugend.example")
        ];
        fcWomen.SocialMediaLinks = [new SocialMediaLink(SocialMediaPlatform.Instagram, "https://www.instagram.com/fc.beispielberg.frauen.example")];
        cultureMusic.SocialMediaLinks = [new SocialMediaLink(SocialMediaPlatform.YouTube, "https://www.youtube.com/@kulturtreff-chor-example")];
        cyclingTouring.SocialMediaLinks = [new SocialMediaLink(SocialMediaPlatform.Bluesky, "https://bsky.app/profile/rsf-ruhrtal.example")];

        var departments = new[]
        {
            tsvYouth, tsvRunning, tsvTableTennis, tsvHandball, handballBambini, handballFirst, handballVeterans, fcYouth, fcWomen, tennisYouth,
            swimmingCompetitive, swimmingCourses, gymnasticsChildren, gymnasticsFitness,
            cyclingTouring, cultureMusic, cultureEvents
        };
        await _context.Departments.AddRangeAsync(departments, cancellationToken);

        // Locations ("Standorte"): linked per organization without inheritance
        var hallNorth = new Location { Id = Guid.NewGuid(), Name = "Sporthalle Nord", Description = "Dreifachhalle mit Tribüne", Street = "Nordring 5", PostalCode = "44787", City = "Bochum" };
        var schoolGym = new Location { Id = Guid.NewGuid(), Name = "Turnhalle Grundschule Süd", Street = "Schulstraße 3", PostalCode = "44799", City = "Bochum" };
        var pool = new Location { Id = Guid.NewGuid(), Name = "Hallenbad Mitte", Street = "Badweg 1", PostalCode = "44801", City = "Bochum" };
        await _context.Locations.AddRangeAsync([hallNorth, schoolGym, pool], cancellationToken);
        tsv.Locations.Add(hallNorth);
        tsvHandball.Locations.Add(hallNorth);
        handballBambini.Locations.Add(schoolGym);
        gymnastics.Locations.Add(schoolGym);
        gymnasticsChildren.Locations.Add(schoolGym);
        swimming.Locations.Add(pool);
        swimmingCourses.Locations.Add(pool);

        ContactAssignment ClubAssignment(User user, Club club, int positionIndex, bool canEdit, string? contextEmail = null, bool confirmed = true, bool receivesLocationInfo = false) => new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClubId = club.Id,
            ContactPositionId = positions[positionIndex].Id,
            CanEdit = canEdit,
            ReceivesLocationInfo = receivesLocationInfo,
            AssignedAt = now.AddDays(-180),
            ContextEmail = contextEmail,
            ContextEmailNormalized = EmailAddressHelper.Normalize(contextEmail),
            ContextEmailVerified = contextEmail != null,
            ContextEmailVerifiedAt = contextEmail != null ? now.AddDays(-30) : null,
            Visibility = !confirmed ? VisibilityLevel.Internal
                : publicContactOrganizations.Contains(club.Id) ? VisibilityLevel.Public : VisibilityLevel.AllUsers,
            ConfirmedAt = confirmed ? now.AddDays(-180) : null
        };

        ContactAssignment DepartmentAssignment(User user, Department department, int positionIndex, bool canEdit, string? contextEmail = null, bool confirmed = true) => new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            DepartmentId = department.Id,
            ContactPositionId = positions[positionIndex].Id,
            CanEdit = canEdit,
            AssignedAt = now.AddDays(-180),
            ContextEmail = contextEmail,
            ContextEmailNormalized = EmailAddressHelper.Normalize(contextEmail),
            ContextEmailVerified = contextEmail != null,
            ContextEmailVerifiedAt = contextEmail != null ? now.AddDays(-30) : null,
            Visibility = !confirmed ? VisibilityLevel.Internal
                : publicContactOrganizations.Contains(department.Id) ? VisibilityLevel.Public : VisibilityLevel.AllUsers,
            ConfirmedAt = confirmed ? now.AddDays(-180) : null
        };

        var assignments = new[]
        {
            // Explicit permission examples used for manual authorization tests.
            ClubAssignment(clubEditor, tsv, 0, true, "vorstand@tsv-musterstadt.example"),
            ClubAssignment(clubReader, tsv, 1, false, receivesLocationInfo: true),
            DepartmentAssignment(departmentEditor, fcYouth, 7, true, "leitung.junioren@fc-beispielberg.example"),
            DepartmentAssignment(departmentReader, fcYouth, 6, false),
            DepartmentAssignment(groupEditor, handballBambini, 6, true, "bambini@tsv-musterstadt.example"),
            ClubAssignment(multiClubEditor, tennis, 8, true),
            ClubAssignment(multiClubEditor, culture, 9, true),

            ClubAssignment(maria, tsv, 2, false),
            DepartmentAssignment(maria, tsvYouth, 4, true),
            ClubAssignment(thomas, fc, 0, true, "vorsitz@fc-beispielberg.example"),
            DepartmentAssignment(thomas, fcWomen, 1, false),
            ClubAssignment(anna, swimming, 0, true),
            DepartmentAssignment(anna, swimmingCourses, 7, true),
            ClubAssignment(cem, gymnastics, 5, true),
            DepartmentAssignment(cem, gymnasticsChildren, 6, false),
            ClubAssignment(sophie, cycling, 0, true),
            DepartmentAssignment(sophie, cyclingTouring, 7, true),
            ClubAssignment(jonas, culture, 1, false),
            DepartmentAssignment(jonas, cultureMusic, 7, true),
            // Pending example: consent (and visibility choice) still outstanding, no rights yet
            DepartmentAssignment(sophie, tsvRunning, 6, true, confirmed: false)
        };
        await _context.ContactAssignments.AddRangeAsync(assignments, cancellationToken);

        Event ClubEvent(
            Club club,
            string title,
            DateTime startUtc,
            DateTime endUtc,
            string location,
            string description,
            bool isAllDay = false,
            bool isHighlight = false) => new()
        {
            Id = Guid.NewGuid(),
            ClubId = club.Id,
            Title = title,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Location = location,
            Description = description,
            IsAllDay = isAllDay,
            IsHighlight = isHighlight
        };

        Event DepartmentEvent(
            Department department,
            string title,
            DateTime startUtc,
            DateTime endUtc,
            string location,
            string description,
            bool isAllDay = false,
            bool isHighlight = false) => new()
        {
            Id = Guid.NewGuid(),
            DepartmentId = department.Id,
            Title = title,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Location = location,
            Description = description,
            IsAllDay = isAllDay,
            IsHighlight = isHighlight
        };

        var events = new[]
        {
            ClubEvent(tsv, "Jahreshauptversammlung", today.AddDays(12).AddHours(18), today.AddDays(12).AddHours(20), "Vereinsheim, Sportplatzweg 1", "Mitgliederversammlung mit Vorstandswahl und Ausblick auf das kommende Vereinsjahr."),
            ClubEvent(tsv, "Großes Sommerfest", today.AddDays(45).AddHours(10), today.AddDays(45).AddHours(19), "Sportanlage Musterstadt", "Sport, Musik, Mitmachangebote und Verpflegung für die ganze Familie.", isHighlight: true),
            DepartmentEvent(tsvYouth, "Jugendcamp", today.AddDays(70), today.AddDays(73), "Jugendherberge am See", "Dreitägiges Trainings- und Freizeitcamp für Kinder und Jugendliche.", isAllDay: true, isHighlight: true),
            DepartmentEvent(tsvRunning, "Offener Lauftreff", today.AddDays(3).AddHours(17), today.AddDays(3).AddHours(18.5), "Eingang Stadtpark", "Alle Leistungsstufen sind willkommen. Eine Anmeldung ist nicht erforderlich."),
            DepartmentEvent(handballFirst, "Heimspiel gegen HSG Nachbarstadt", today.AddDays(10).AddHours(17), today.AddDays(10).AddHours(19), "Sporthalle Nord", "Bezirksliga-Heimspiel der 1. Mannschaft. Der Eintritt ist frei."),
            DepartmentEvent(handballBambini, "Bambini-Spielfest", today.AddDays(24).AddHours(9), today.AddDays(24).AddHours(13), "Sporthalle Nord", "Spielfest für Kinder von 4 bis 6 Jahren mit Mitmachstationen."),
            DepartmentEvent(tsvTableTennis, "Vereinsmeisterschaft", today.AddDays(25).AddHours(9), today.AddDays(25).AddHours(17), "Sporthalle Nord", "Einzel- und Doppelmeisterschaft, Zuschauer sind willkommen."),

            ClubEvent(fc, "Heimspiel gegen SV Nachbarstadt", today.AddDays(8).AddHours(13), today.AddDays(8).AddHours(15), "Beispielberg-Arena", "Meisterschaftsspiel der ersten Mannschaft.", isHighlight: true),
            DepartmentEvent(fcYouth, "Probetraining U12", today.AddDays(5).AddHours(15), today.AddDays(5).AddHours(17), "Jugendplatz 2", "Bitte Sportkleidung und eine Trinkflasche mitbringen."),

            ClubEvent(tennis, "Saisoneröffnung", today.AddDays(18).AddHours(9), today.AddDays(18).AddHours(16), "Tennisanlage Stadtpark", "Schleifchenturnier, Schnuppertraining und gemeinsames Grillen.", isHighlight: true),
            DepartmentEvent(tennisYouth, "Ferien-Schnupperkurs", today.AddDays(32), today.AddDays(36), "Plätze 3–5", "Kostenloser Schnupperkurs für Kinder von acht bis zwölf Jahren.", isAllDay: true),

            ClubEvent(swimming, "Vereinsabend", today.AddDays(14).AddHours(17), today.AddDays(14).AddHours(20), "Café Wellenbad", "Informationen für Mitglieder, Eltern und Aktive."),
            DepartmentEvent(swimmingCompetitive, "Ruhr-Cup", today.AddDays(28).AddHours(7), today.AddDays(28).AddHours(18), "Hallenbad West", "Regionaler Schwimmwettkampf mit Vereinen aus dem Ruhrgebiet.", isHighlight: true),
            DepartmentEvent(swimmingCourses, "Anmeldestart Seepferdchen", today.AddDays(6).AddHours(8), today.AddDays(6).AddHours(9), "Online", "Freischaltung der Plätze für den nächsten Kursblock."),

            ClubEvent(gymnastics, "100 Jahre TG Süd", today.AddDays(100), today.AddDays(102), "Turnhalle und Außengelände", "Jubiläumswochenende mit Showprogramm und Vereinsgeschichte.", isAllDay: true, isHighlight: true),
            DepartmentEvent(gymnasticsChildren, "Eltern-Kind-Turnen", today.AddDays(4).AddHours(14), today.AddDays(4).AddHours(15), "Turnhalle Süd", "Offene Kennenlernstunde für Kinder von zwei bis vier Jahren."),

            ClubEvent(cycling, "Frühjahrs-Radtour", today.AddDays(16).AddHours(8), today.AddDays(16).AddHours(15), "Rathausplatz", "Geführte 65-km-Tour durchs Ruhrtal. Helmpflicht.", isHighlight: true),

            ClubEvent(culture, "Fest der Vielfalt", today.AddDays(55).AddHours(11), today.AddDays(55).AddHours(21), "Rathausplatz", "Bühnenprogramm, internationale Küche und Angebote lokaler Initiativen.", isHighlight: true),
            DepartmentEvent(cultureMusic, "Offene Chorprobe", today.AddDays(9).AddHours(17), today.AddDays(9).AddHours(19), "Kulturhaus, Saal 2", "Unverbindlich mitsingen und den Chor kennenlernen."),

            // Past entries make archive, date filtering and empty-state transitions testable.
            ClubEvent(tsv, "Neujahrsempfang", today.AddDays(-45).AddHours(10), today.AddDays(-45).AddHours(13), "Vereinsheim", "Rückblick und Ehrung langjähriger Mitglieder."),
            ClubEvent(fc, "Winterturnier", today.AddDays(-20).AddHours(8), today.AddDays(-20).AddHours(17), "Sporthalle Beispielberg", "Hallenturnier der Seniorenmannschaften."),
        };
        await _context.Events.AddRangeAsync(events, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Development data seeded successfully: {ClubCount} clubs, {DepartmentCount} departments, {UserCount} users, {EventCount} events",
            clubs.Length,
            departments.Length,
            users.Length,
            events.Length);
    }

    private async Task SeedAppSettingsAsync(CancellationToken cancellationToken)
    {
        var settingsToSeed = new Dictionary<string, string?>
        {
            ["PrivacyPolicyUrl"] = "",
            ["ContactName"] = "",
            ["ContactEmail"] = _appSettings.Value.SystemAdminEmail
        };

        foreach (var (key, defaultValue) in settingsToSeed)
        {
            var exists = await _context.AppSettings.AnyAsync(s => s.Key == key, cancellationToken);
            if (!exists)
            {
                _context.AppSettings.Add(new AppSetting
                {
                    Id = Guid.NewGuid(),
                    Key = key,
                    Value = defaultValue ?? "",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                _logger.LogInformation("Seeded app setting '{Key}'", key);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
