using System.Security.Claims;
using System.Text.Json;
using Igevo.Vernetzt.Core.Entities;
using Igevo.Vernetzt.Core.Enums;
using Igevo.Vernetzt.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Igevo.Vernetzt.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context for the Vernetzt application.
/// Also hosts the ASP.NET Core Identity user store (accounts, tokens, passkeys; no Identity roles).
/// </summary>
public class VernetztDbContext : IdentityUserContext<
    AppIdentityUser,
    Guid,
    IdentityUserClaim<Guid>,
    IdentityUserLogin<Guid>,
    IdentityUserToken<Guid>,
    IdentityUserPasskey<Guid>>,
    IDataProtectionKeyContext
{
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public VernetztDbContext(DbContextOptions<VernetztDbContext> options, IHttpContextAccessor? httpContextAccessor = null) : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Identity schema version 3 adds the passkey table.
    /// Note: the model itself is built from IdentityOptions.Stores.SchemaVersion (set in Program.cs);
    /// contexts created without those options (e.g. in tests) must configure it as well.
    /// </summary>
    protected override Version SchemaVersion => IdentitySchemaVersions.Version3;

    /// <summary>
    /// All timestamps are stored as UTC. SQLite keeps no time zone information, so values read back
    /// are marked as UTC again (otherwise they would be serialized without the "Z" suffix).
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    /// <summary>
    /// Domain users. Hides <c>IdentityUserContext.Users</c> (the Identity accounts, see <see cref="IdentityAccounts"/>).
    /// </summary>
    public new DbSet<User> Users => Set<User>();

    /// <summary>
    /// ASP.NET Core Identity accounts (1:1 with <see cref="Users"/>).
    /// </summary>
    public DbSet<AppIdentityUser> IdentityAccounts => Set<AppIdentityUser>();

    /// <summary>
    /// ASP.NET Core Data Protection key ring. Persisted so that sessions, login links and
    /// protected calendar feed tokens survive restarts and redeployments.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>
    /// Redeemed login-link tokens (single-use enforcement).
    /// </summary>
    public DbSet<ConsumedLoginToken> ConsumedLoginTokens => Set<ConsumedLoginToken>();
    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<ClubType> ClubTypes => Set<ClubType>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<ContactAssignment> ContactAssignments => Set<ContactAssignment>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventMedia> EventMediaItems => Set<EventMedia>();
    public DbSet<ContactAssignmentEmailVerificationToken> ContactAssignmentEmailVerificationTokens => Set<ContactAssignmentEmailVerificationToken>();
    public DbSet<LoginEmailHintRateLimit> LoginEmailHintRateLimits => Set<LoginEmailHintRateLimit>();
    public DbSet<ContactPosition> ContactPositions => Set<ContactPosition>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Invitation tokens
    public DbSet<InvitationToken> InvitationTokens => Set<InvitationToken>();

    // Application settings (admin-managed key-value store)
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    // Email relay tokens
    public DbSet<EmailRelayToken> EmailRelayTokens => Set<EmailRelayToken>();
    public DbSet<CalendarFeedToken> CalendarFeedTokens => Set<CalendarFeedToken>();
    public DbSet<CardDavToken> CardDavTokens => Set<CardDavToken>();

    // Central holiday calendar (school holidays, public holidays)
    public DbSet<HolidaySource> HolidaySources => Set<HolidaySource>();
    public DbSet<HolidayEntry> HolidayEntries => Set<HolidayEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();

            // Store emails trimmed and lower-case so the unique index and lookups are case-insensitive.
            entity.Property(e => e.Email).HasConversion(
                v => v.Trim().ToLowerInvariant(),
                v => v);

            // Ignore computed property
            entity.Ignore(e => e.FullName);

            // Store Claims as JSON
            var claimsConverter = new ValueConverter<List<UserClaim>, string>(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<List<UserClaim>>(v, JsonSerializerOptions.Default) ?? new List<UserClaim>());

            var claimsComparer = new ValueComparer<List<UserClaim>>(
                (c1, c2) => c1!.SequenceEqual(c2!),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList());

            entity.Property(e => e.Claims)
                .HasConversion(claimsConverter)
                .Metadata.SetValueComparer(claimsComparer);
        });

        // Identity account shares its primary key with the domain user and is removed with it.
        modelBuilder.Entity<AppIdentityUser>(entity =>
        {
            entity.HasOne<User>()
                .WithOne()
                .HasForeignKey<AppIdentityUser>(a => a.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure ConsumedLoginToken entity (single-use login links)
        modelBuilder.Entity<ConsumedLoginToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.TokenHash).HasMaxLength(64);
        });

                // Configure Club entity
        modelBuilder.Entity<Club>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.ClubTypeId);
            ConfigureSocialMediaLinks(entity.Property(e => e.SocialMediaLinks));

            entity.HasOne(e => e.ClubType)
                .WithMany(ct => ct.Clubs)
                .HasForeignKey(e => e.ClubTypeId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Departments)
                .WithOne(d => d.Club)
                .HasForeignKey(d => d.ClubId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ContactAssignments)
                .WithOne(ca => ca.Club)
                .HasForeignKey(ca => ca.ClubId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Department entity
        modelBuilder.Entity<Department>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.ClubId);
            entity.HasIndex(e => e.ParentDepartmentId);
            entity.Ignore(e => e.IsGroup);
            ConfigureSocialMediaLinks(entity.Property(e => e.SocialMediaLinks));

            entity.HasMany(e => e.ContactAssignments)
                .WithOne(ca => ca.Department)
                .HasForeignKey(ca => ca.DepartmentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Groups: one level below a department. No database cascade (NO ACTION) to avoid a second cascade path
            // next to Club -> Departments; DepartmentService removes groups before their department, and a club
            // delete removes departments and groups in one cascade.
            entity.HasOne(e => e.ParentDepartment)
                .WithMany(e => e.Groups)
                .HasForeignKey(e => e.ParentDepartmentId)
                .OnDelete(DeleteBehavior.ClientCascade);
        });

        // Configure ContactAssignment entity
        modelBuilder.Entity<ContactAssignment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ClubId);
            entity.HasIndex(e => e.DepartmentId);
            entity.HasIndex(e => e.ContextEmailNormalized);
            entity.Property(e => e.ContextEmail).HasMaxLength(255);
            entity.Property(e => e.ContextEmailNormalized).HasMaxLength(255);

            // Unique constraint: same user can only be assigned once per club/department
            entity.HasIndex(e => new { e.UserId, e.ClubId })
                .IsUnique()
                .HasFilter("\"ClubId\" IS NOT NULL");
            entity.HasIndex(e => new { e.UserId, e.DepartmentId })
                .IsUnique()
                .HasFilter("\"DepartmentId\" IS NOT NULL");

            entity.HasOne(e => e.User)
                .WithMany(u => u.ContactAssignments)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ContactPosition)
                .WithMany(cp => cp.ContactAssignments)
                .HasForeignKey(e => e.ContactPositionId)
                .OnDelete(DeleteBehavior.SetNull);

            // Check constraint: must have either ClubId OR DepartmentId, not both or neither
            entity.ToTable(t => t.HasCheckConstraint("CK_ContactAssignment_ClubOrDepartment",
                "(\"ClubId\" IS NOT NULL AND \"DepartmentId\" IS NULL) OR (\"ClubId\" IS NULL AND \"DepartmentId\" IS NOT NULL)"));
        });

        // Configure Event entity
        modelBuilder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClubId);
            entity.HasIndex(e => e.DepartmentId);
            entity.HasIndex(e => e.StartUtc);
            entity.HasIndex(e => e.EndUtc);
            entity.HasIndex(e => e.IsHighlight);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(5000);
            entity.Property(e => e.Location).HasMaxLength(300);

            entity.HasOne(e => e.Club)
                .WithMany(c => c.Events)
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Department)
                .WithMany(d => d.Events)
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Media)
                .WithOne(m => m.Event)
                .HasForeignKey(m => m.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(t => t.HasCheckConstraint("CK_Event_ClubOrDepartment",
                "(\"ClubId\" IS NOT NULL AND \"DepartmentId\" IS NULL) OR (\"ClubId\" IS NULL AND \"DepartmentId\" IS NOT NULL)"));

            entity.ToTable(t => t.HasCheckConstraint("CK_Event_EndAfterStart",
                "\"EndUtc\" > \"StartUtc\""));
        });

        modelBuilder.Entity<EventMedia>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EventId);
            entity.Property(e => e.FilePath).HasMaxLength(500);
            entity.Property(e => e.FileName).HasMaxLength(255);
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.FocusX).HasDefaultValue(0.5);
            entity.Property(e => e.FocusY).HasDefaultValue(0.5);
            entity.Property(e => e.Usages).HasDefaultValue(EventMediaUsage.None);
            entity.Property(e => e.VariantWidths).HasMaxLength(100);
            entity.Ignore(e => e.HasPreview);

            entity.HasOne(e => e.Event)
                .WithMany(ev => ev.Media)
                .HasForeignKey(e => e.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ContactAssignmentEmailVerificationToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.ContactAssignmentId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.TokenHash).HasMaxLength(128);
            entity.Property(e => e.EmailNormalizedSnapshot).HasMaxLength(255);

            entity.HasOne(e => e.ContactAssignment)
                .WithMany()
                .HasForeignKey(e => e.ContactAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoginEmailHintRateLimit>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EmailHash).IsUnique();
            entity.Property(e => e.EmailHash).HasMaxLength(128);
        });

        // Configure ClubType entity
        modelBuilder.Entity<ClubType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
        });

        // Configure ContactPosition entity
        modelBuilder.Entity<ContactPosition>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
        });

        // Configure AuditLog entity
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.EntityId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.Action);

            // Store OldValues and NewValues as unbounded text
            entity.Property(e => e.OldValues).HasColumnType("text");
            entity.Property(e => e.NewValues).HasColumnType("text");
        });

        // Configure AppSetting entity (key-value store)
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Key).IsUnique();
            entity.Property(e => e.Key).HasMaxLength(100);
        });

        // Configure InvitationToken entity
        modelBuilder.Entity<InvitationToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.Token).HasMaxLength(128);
            entity.Property(e => e.Email).HasMaxLength(256);
            entity.Property(e => e.ContextEmail).HasMaxLength(255);

            // Pre-linked contact assignment FKs
            entity.HasOne(e => e.Club)
                .WithMany()
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.Department)
                .WithMany()
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ContactPosition)
                .WithMany()
                .HasForeignKey(e => e.ContactPositionId)
                .OnDelete(DeleteBehavior.SetNull);

            // Store PendingClaims as JSON
            var pendingClaimsConverter = new ValueConverter<List<UserClaim>, string>(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<UserClaim>()
                    : JsonSerializer.Deserialize<List<UserClaim>>(v, JsonSerializerOptions.Default) ?? new List<UserClaim>());

            var pendingClaimsComparer = new ValueComparer<List<UserClaim>>(
                (c1, c2) => c1!.SequenceEqual(c2!),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList());

            entity.Property(e => e.PendingClaims)
                .HasConversion(pendingClaimsConverter)
                .Metadata.SetValueComparer(pendingClaimsComparer);

            // ClubId XOR DepartmentId (or both null)
            entity.ToTable(t => t.HasCheckConstraint("CK_InvitationToken_ClubOrDepartment",
                "\"ClubId\" IS NULL OR \"DepartmentId\" IS NULL"));
        });

        // Configure EmailRelayToken entity
        modelBuilder.Entity<EmailRelayToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenAddress).IsUnique();
            entity.HasIndex(e => e.CreatedByUserId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.TokenAddress).HasMaxLength(256);
            entity.Property(e => e.SenderEmail).HasMaxLength(256);
            entity.Property(e => e.SubjectHint).HasMaxLength(200);

            // Store RecipientEmails as JSON
            var recipientEmailsConverter = new ValueConverter<List<string>, string>(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default) ?? new List<string>());

            var recipientEmailsComparer = new ValueComparer<List<string>>(
                (c1, c2) => c1!.SequenceEqual(c2!),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList());

            entity.Property(e => e.RecipientEmails)
                .HasConversion(recipientEmailsConverter)
                .Metadata.SetValueComparer(recipientEmailsComparer);
        });

        modelBuilder.Entity<CalendarFeedToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.Scope);
            entity.HasIndex(e => e.ClubId);
            entity.HasIndex(e => e.DepartmentId);
            entity.HasIndex(e => e.IsRevoked);
            entity.Property(e => e.TokenHash).HasMaxLength(128);
            entity.Property(e => e.ProtectedToken).HasMaxLength(2000);

            entity.HasOne(e => e.Club)
                .WithMany()
                .HasForeignKey(e => e.ClubId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Department)
                .WithMany()
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_CalendarFeedToken_ScopeAssignment",
                "(\"Scope\" = 0 AND \"ClubId\" IS NOT NULL AND \"DepartmentId\" IS NULL) OR " +
                "(\"Scope\" = 1 AND \"ClubId\" IS NULL AND \"DepartmentId\" IS NOT NULL) OR " +
                "(\"Scope\" = 2 AND \"ClubId\" IS NULL AND \"DepartmentId\" IS NULL)"));
        });

        modelBuilder.Entity<CardDavToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.IsRevoked });
            entity.Property(e => e.TokenHash).HasMaxLength(128);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HolidaySource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.Url).HasMaxLength(2000);
            entity.Property(e => e.LastSyncError).HasMaxLength(1000);
        });

        modelBuilder.Entity<HolidayEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.StartDate, e.EndDate });
            entity.HasIndex(e => e.SourceId);
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.ExternalUid).HasMaxLength(500);

            entity.HasOne(e => e.Source)
                .WithMany(s => s.Entries)
                .HasForeignKey(e => e.SourceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(t => t.HasCheckConstraint(
                "CK_HolidayEntry_DateRange",
                "\"EndDate\" >= \"StartDate\""));
        });
    }

    private static readonly JsonSerializerOptions SocialMediaLinkJsonOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// Stores social media links as JSON text (platform names as strings, so the column stays readable).
    /// </summary>
    private static void ConfigureSocialMediaLinks(PropertyBuilder<List<SocialMediaLink>> property)
    {
        var converter = new ValueConverter<List<SocialMediaLink>, string>(
            v => JsonSerializer.Serialize(v, SocialMediaLinkJsonOptions),
            v => JsonSerializer.Deserialize<List<SocialMediaLink>>(v, SocialMediaLinkJsonOptions) ?? new List<SocialMediaLink>());

        var comparer = new ValueComparer<List<SocialMediaLink>>(
            (a, b) => a!.SequenceEqual(b!),
            v => v.Aggregate(0, (hash, link) => HashCode.Combine(hash, link.GetHashCode())),
            v => v.ToList());

        property.HasConversion(converter)
            .HasDefaultValue(new List<SocialMediaLink>())
            .Metadata.SetValueComparer(comparer);
    }

    /// <summary>
    /// Override SaveChanges to automatically set audit fields.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetAuditFields();
        SyncIdentityAccounts();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <summary>
    /// Override SaveChangesAsync to automatically set audit fields.
    /// </summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetAuditFields();
        SyncIdentityAccounts();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void SetAuditFields()
    {
        var entries = ChangeTracker.Entries<BaseEntity>();
        var currentUserId = GetCurrentUserId();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    if (currentUserId.HasValue)
                    {
                        entry.Entity.CreatedById = currentUserId.Value;
                        entry.Entity.UpdatedById = currentUserId.Value;
                    }
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    if (currentUserId.HasValue)
                    {
                        entry.Entity.UpdatedById = currentUserId.Value;
                    }
                    // Don't overwrite CreatedAt or CreatedById
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Property(e => e.CreatedById).IsModified = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Keeps the Identity account in step with the domain user:
    /// a new user gets an account in the same transaction, an email change is mirrored.
    /// Deletion is handled by the cascading foreign key.
    /// </summary>
    private void SyncIdentityAccounts()
    {
        var userEntries = ChangeTracker.Entries<User>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        foreach (var entry in userEntries)
        {
            var user = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                var alreadyTracked = ChangeTracker.Entries<AppIdentityUser>().Any(a => a.Entity.Id == user.Id);
                if (!alreadyTracked)
                {
                    IdentityAccounts.Add(AppIdentityUser.CreateFor(user));
                }

                continue;
            }

            if (!entry.Property(u => u.Email).IsModified)
            {
                continue;
            }

            var account = IdentityAccounts.Find(user.Id);
            account?.ApplyEmail(user.Email);
        }
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
