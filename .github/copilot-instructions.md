# Igevo.Vernetzt - Copilot Instructions

## Project Overview
**Igevo.Vernetzt** is a web application for connecting clubs and organizations (e.g. in a city or region): their departments, groups, contact persons and events. The primary goal is self-administration by clubs while maintaining central oversight by a parent organization.

### Key Information
- **Project Name**: Vernetzt
- **Namespace**: Igevo.Vernetzt
- **Target Framework**: .NET 10.0
- **Architecture**: Vue single-page application + REST API (Aspire AppHost orchestration)
- **Language**: All code and comments in **English**, UI in **German**
- **Development**: Dev Container based

## Technology Stack

### Backend
- **Framework**: ASP.NET Core 10.0 (REST API + static Vue hosting)
- **Database**: SQLite via Entity Framework Core (Microsoft.EntityFrameworkCore.Sqlite), single file in WAL mode; daily backups via `VACUUM INTO` (`DatabaseBackupService`)
- **ORM**: EF Core with automatic migrations on startup
- **Authentication**: ASP.NET Core Identity (.NET 10): login link by email, passkeys, cookie session
- **Validation**: FluentValidation (business rule validation in Infrastructure/Validators)
- **Logging & Telemetry**: Microsoft.Extensions.Logging (`ILogger<T>`, structured message templates) plus OpenTelemetry (logs, metrics, traces via OTLP, see `ServiceDefaults`); business metrics in `Infrastructure/Observability/VernetztMetrics.cs`
- **File Storage**: Local file system (interface-based for future S3 migration)
- **Email**: SMTP via Mailpit (managed by Aspire AppHost in development)
- **Testing**: MSTest + Moq (Unit Tests + Integration Tests)

### Frontend
- **Framework**: Vue 3, Vite, strict TypeScript and Vuetify
- **Language**: German UI, English code
- **API Communication**: Same-origin REST API calls through the central fetch client
- **Hosting**: Static files served by ASP.NET Core backend

### Infrastructure
- **Orchestration**: Aspire AppHost (manages Server and Mailpit in development)
- **Containerization**: Docker Compose for production; Aspire for development
- **Deployment**: Single instance per organization (no multi-tenancy)
- **Hosting**: Cloud VM or on-premises server

## Architecture Principles

### Project Structure
```
Igevo.Vernetzt.slnx
├── Igevo.Vernetzt.AppHost           # Aspire AppHost (orchestrates all services in development)
├── Igevo.Vernetzt.Server            # ASP.NET Core backend (REST API + Vue static hosting)
├── Igevo.Vernetzt.WebApp            # Vue/Vite single-page application
├── Igevo.Vernetzt.Core              # Domain models, interfaces, DTOs
├── Igevo.Vernetzt.Infrastructure    # EF Core DbContext, repositories, validators
├── Igevo.Vernetzt.ServiceDefaults   # Shared Aspire service defaults (health checks, OpenTelemetry)
└── Igevo.Vernetzt.Tests             # MSTest unit & integration tests
```

### Design Patterns
- **Repository Pattern**: Abstract data access via interfaces (EF Core repositories)
- **Service Layer**: Business logic separated from controllers
- **Dependency Injection**: Use built-in DI container
- **Interface Segregation**: Small, focused interfaces for flexibility
- **Future-Proof**: Design for easy migration to cloud storage (Local → S3)

### Coding Standards
- **Language**: All code, comments, variable names, and XML documentation in English
- **Naming Conventions**: Follow Microsoft C# conventions
- **Async/Await**: Use async methods with CancellationToken support
- **Error Handling**: Use structured exceptions, return appropriate HTTP status codes
- **Logging**: Use ILogger for all logging
- **Validation**: DataAnnotations + FluentValidation (validators in Infrastructure/Validators)
- **API Responses**: Consistent response structure (success/error)

## User Documentation (docmd)

The end-user documentation for Vernetzt is part of this repository and must be maintained together with product changes.

### Location and Build
- Source: `user-docs/docs/`
- Navigation/Menu tree: `user-docs/docmd.config.js`
- Build + integration script: `scripts/build-help-docs.sh`
- Generated target: `Igevo.Vernetzt.WebApp/wwwroot/help/`

### Editorial Rules (must follow)
- Audience: non-technical end users.
- Language: German, **du** form.
- Always call the application **"Vernetzt"**.
- Document features page-by-page (aligned with app navigation and role-based areas).
- Use docmd styling/components (for example callout alerts) for warnings, tips, and key notes.
- Keep these topics explicitly covered and current:
  - Login options (login link by email + Passkey)
  - PWA installation, including the note that installed Vernetzt uses Passkey login
  - Data processing/storage/access visibility, including a clear matrix-style overview
  - Email privacy behavior (sending without exposing recipient addresses)

### Change Policy
- When app UI, permissions, onboarding, auth, or privacy-relevant behavior changes, update user docs in the same change set.
- Keep doc navigation (`docmd.config.js`) in sync with added/removed pages.
- Validate by running `./scripts/build-help-docs.sh`.

## Domain Model

### Core Entities
```
BaseEntity (inherited by all entities)
  ├─ Id (Guid)
  ├─ CreatedAt, CreatedById
  └─ UpdatedAt, UpdatedById

User (combines user account and contact person)
  ├─ Email (unique, case-insensitive via DB collation)
  ├─ Claims/Roles (List<UserClaim> stored as JSON)
  ├─ Personal information (FirstName, LastName, PhoneNumber)
  ├─ Visibility settings (EmailVisibility, PhoneVisibility)
  ├─ EmailVerified, LastLoginAt
  └─ ContactAssignments (navigation property)

Club
  ├─ Name, Description (public plain-text info), ClubTypeId (FK to ClubType)
  ├─ Address (Street, PostalCode, City)
  ├─ WebsiteUrl, Email, SocialMediaLinks (JSON column, optional)
  ├─ ContactPersonsVisibility
  ├─ Departments (navigation property, includes groups)
  ├─ ContactAssignments (navigation property)
  └─ Events (navigation property)

Department (department, or – with ParentDepartmentId – a group such as a team or course)
  ├─ Name, Description, ClubId (FK to Club; a group has the same ClubId as its parent)
  ├─ ParentDepartmentId (nullable self-FK, max. one level: Club > Department > Group)
  ├─ Address, WebsiteUrl, Email, SocialMediaLinks (optional; groups have no own address/logo in the UI)
  ├─ ContactPersonsVisibility
  ├─ Club, ParentDepartment, Groups (navigation properties)
  ├─ ContactAssignments (navigation property)
  └─ Events (navigation property)

Event (always public – Vernetzt does not organize internal events)
  ├─ Title, Description, Location, StartUtc, EndUtc, IsAllDay, IsHighlight
  ├─ ClubId XOR DepartmentId (department events include the events of their groups)
  └─ Media (images, posters, attachments)

ContactAssignment (proper entity, not just junction)
  ├─ UserId, ClubId (nullable), DepartmentId (nullable)
  ├─ ContactPositionId (optional)
  ├─ Notes (assignment details/free text)
  ├─ ContextEmail (optional, never for login)
  ├─ ContextEmailNormalized
  ├─ ContextEmailVerified, ContextEmailVerifiedAt
  ├─ ContextEmailVisibility (VisibilityLevel)
  ├─ AssignedAt
  └─ Navigation properties to User, Club, Department, ContactPosition

ClubType (admin-managed entity)
  ├─ Name, Description
  └─ Clubs (navigation property)

AuditLog (permanent records)
  ├─ EntityType, EntityId
  ├─ Action (enum: Create, Update, Delete, Login, Logout, View)
  ├─ UserId (Guid reference)
  └─ Timestamp, IpAddress, OldValues, NewValues
```

### Key Relationships
- **Users**: Every contact person is also a user, but not every user is a contact person
- **ContactAssignment**: Proper entity linking users to clubs/departments/groups with position
  - Check constraint: ClubId XOR DepartmentId (exactly one must be set; groups use DepartmentId)
  - `CanEdit` grants edit rights for that entity and everything below it (club > departments > groups), never upwards
- **Groups**: Departments with `ParentDepartmentId`. Created/deleted by editors of the parent department; deleting a department deletes its groups first (no DB cascade on the self-FK). In the UI always show the full path (`Club › Department › Group`)
- **Events**: All events are public (portal, iCal feeds). There is no event visibility
- **ClubType**: Admin-managed entity, clubs reference via ClubTypeId (FK)
- **Visibility**: 
  - User email/phone have individual VisibilityLevel (Internal, AllUsers, Public)
  - Club/Department contact persons have ContactPersonsVisibility setting
- **No Organization entity**: Organization management not needed for MVP

### Enums
- **UserClaim**: SystemAdmin, Manager, DataReader
- **VisibilityLevel**: Internal (0), AllUsers (1), Public (2)
- **AuditAction**: Create, Update, Delete, Login, Logout, View

## Security & Privacy (CRITICAL)

### Authentication
- **Method**: ASP.NET Core Identity – single-use login link by email (`LoginLink` token provider) and passkeys (Identity's built-in WebAuthn support)
- **Session**: HttpOnly Identity cookie `vernetzt.session`; security stamp re-validated every few minutes ("log out on all devices" rotates it)
- **Accounts**: `AppIdentityUser` (AspNetUsers) is a 1:1 companion of `User` (same Id), synced in `VernetztDbContext.SaveChanges`; roles stay on `User.Claims`
- **Never** write custom token/crypto code for auth – use Identity APIs (`UserManager`, `SignInManager`)
- **Device Tracking**: Track user devices for security

### Authorization
- **Claims-Based Access Control**:
  - **SystemAdmin** (UserClaim enum): Full system access
  - **Manager** (UserClaim enum): Manages clubs, master data and users
  - **DataReader** (UserClaim enum): Read-only access to all data
  - **Per-entity edit rights**: `ContactAssignment.CanEdit` on a club, department or group (inherits downwards)
  - All authenticated users can read all clubs, departments, groups and events
- **Claims Storage**: User.Claims property (List<UserClaim>)
- **Data Isolation**: Users can only edit their own club/department data
- **Assignment Security**: Access checks for communication targets must be enforced per `ContactAssignment` (no implicit trust of client-submitted IDs)
- **Audit Trail**: All modifications logged with UserId (Guid), action (AuditAction enum), timestamp

### GDPR Compliance (Implemented)
- **Right to be Forgotten**: Complete user data deletion via `/api/gdpr/delete-account`
- **Data Export**: Full data export via `/api/gdpr/export` (JSON download)
- **Direct Deletion**: User data and related tokens are directly deleted (no anonymization)
- **Data Minimization**: Only store necessary information
- **Audit Logging**: Track who accessed/modified what data
- **GDPR Info Endpoint**: `/api/gdpr/info` provides user rights information

### Security Best Practices
- **Strict Email Separation**:
  - `User.Email` is the only authentication/login address
  - assignment `ContextEmail` is communication-only and must never be used for auth
- **Context Email Verification**:
  - unverified context emails are never used for outbound delivery
  - verification is token-based and bound to assignment + email snapshot
- **Visibility Rules**:
  - context email visibility follows `VisibilityLevel`
  - unverified context emails are only visible to owner/manager/system admin
- **Recipient Resolution**:
  - prefer assignment-based resolution (`RecipientAssignmentIds`)
  - perform final dedupe by normalized email
  - reject unauthorized assignment IDs with explicit forbidden response
- **Badge Consistency**:
  - expose deterministic badge fields (`EmailBadgeType`, `EmailBadgeLabel`, `EmailBadgeReason`) so UI can explain duplicates/fallbacks clearly
- **Audit Coverage**:
  - log context-email set/update/remove, verification resend/confirm, recipient resolution, and login-hint sends
- **Input Validation**: FluentValidation + DataAnnotations
- **SQL Injection**: Prevented by EF Core parameterized queries
- **XSS Protection**: Sanitize rich HTML on the server and with DOMPurify for client previews
- **CSRF Protection**: Use built-in ASP.NET Core protections
- **Rate Limiting**: Implement rate limiting on API endpoints
- **HTTPS Only**: Enforce HTTPS in production
- **Secure Headers**: Add security headers (CSP, HSTS, etc.)

## Development Workflow

### Git Strategy
- **GitHub Flow**: `main` is always releasable and protected by a ruleset (PR + green CI required, no force push)
- **Branches**: short-lived `feat/…`, `fix/…`, `chore/…`, `docs/…`; merged via squash merge
- **Commit Messages / PR titles**: Conventional Commits in English (`feat: …`, `fix: …`, `chore(deps): …`)
- **Releases**: explicit, via the `Release` workflow (`workflow_dispatch` with `bump` or `version`); never tag manually
- **Language**: code, comments and commit messages in English; README, community files, issues and user docs in German
- **License**: EUPL-1.2; only add dependencies with EUPL-compatible licenses

### Testing Strategy
- **Unit Tests**: MSTest for business logic, services, and utilities
- **Integration Tests**: MSTest for API endpoints and database operations
- **Test Coverage**: Aim for >80% coverage on business logic
- **Test Naming**: `MethodName_Scenario_ExpectedResult`

### Local Development
- **Dev Container**: Recommended for consistent development environment
- **Aspire AppHost**: `dotnet watch run` in AppHost project starts all services (Server, Mailpit)
- **Aspire Dashboard**: Available at http://localhost:15215 for monitoring all resources
- **Hot Reload**: Use .NET Hot Reload via `dotnet watch` on AppHost
- **Local SMTP**: Mailpit (system emails) runs on http://localhost:56080 (managed by Aspire)
- **Database**: SQLite file `Igevo.Vernetzt.Server/App_Data/vernetzt.db` (auto-migrates on startup); tests use SQLite too (in-memory per unit test, temporary file per WebApplicationFactory host)
- **Docker Compose**: Still available for production deployment

## API Design

### RESTful Conventions
- **GET**: Retrieve resources (200 OK, 404 Not Found)
- **POST**: Create resources (201 Created, 400 Bad Request)
- **PUT**: Update resources (200 OK, 404 Not Found)
- **DELETE**: Remove resources (204 No Content, 404 Not Found)
- **PATCH**: Partial updates (200 OK, 404 Not Found)

### API Routing
- **Convention**: `/api/[controller]` (e.g., `/api/clubs`, `/api/departments`)
- **No versioning prefix** currently in use

### Response Format
```json
// Success
{
  "success": true,
  "data": { ... }
}

// Error
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Validation failed",
    "details": [ ... ]
  }
}
```

### Authentication Headers
- **Authorization**: session cookie `vernetzt.session` (HttpOnly, SameSite=Strict); unsafe cookie requests must come from `App:BaseUrl` origin
- **X-Device-Id**: Track devices (optional)

## File Storage

### Current Implementation
- **Location**: Local file system
- **Structure**: Organized by entity type and ID
- **Interface**: `IFileStorageService` for future migration

### File Types
- **Profile Images**: JPG, PNG (max 5MB)
- **Documents**: PDF, DOCX (max 10MB)
- **Validation**: Check file types and sizes server-side

### Future Migration Path
- Design for easy migration to S3/Azure Blob Storage
- Use interface abstraction

## Email Features

### Use Cases
- Login link authentication
- Notifications to club managers
- Bulk emails to selected clubs/contacts
- Event reminders
- System notifications

### SMTP Configuration
- **Development**: Mailpit via Aspire (http://localhost:56080)
- **Production**: Configurable SMTP server in appsettings.json
- **Templates**: Use Razor templates for email content

## Additional API Endpoints

### Search & Filtering
- `GET /api/clubs/search` - Paginated search with filters (ClubTypeId, City, SearchTerm)
- `GET /api/clubs/by-type` - Clubs grouped by ClubType
- `GET /api/clubs/cities` - List of unique cities

### Statistics
- `GET /api/stats/dashboard` - Dashboard statistics (clubs, departments, users, assignments)
- `GET /api/stats/summary` - Lightweight summary for polling
- `GET /api/stats/my-clubs` - Current user's club assignments

### GDPR
- `GET /api/gdpr/export` - Export own data
- `GET /api/gdpr/export/download` - Download data as JSON file
- `POST /api/gdpr/delete-account` - Delete own account (requires email confirmation)
- `GET /api/gdpr/info` - GDPR information page

## Planned Features (Future)

### Phase 1 (MVP) - ✅ Complete
- ✅ User authentication (login link, passkeys via ASP.NET Core Identity)
- ✅ Club management (CRUD)
- ✅ Department management
- ✅ Contact person management
- ✅ Basic permissions
- ✅ Audit logging
- ✅ GDPR compliance (export, delete, daily cleanup job)
- ✅ Search/filtering with pagination

### Phase 2
- Contact overview/directory
- CardDAV feed for contacts
- ✅ Email relay (token-based email forwarding to clubs/contacts)

### Phase 3
- Event/Calendar management
- iCal subscription feeds
- File uploads (documents, images)

### Phase 4
- Passkey authentication
- Enhanced reporting
- Advanced analytics
- Public-facing club pages

## Important Notes

### Initial Setup & Configuration
- **System Administrator**: The email address of the initial system administrator must be configured in `appsettings.json` under `App:SystemAdminEmail`
- **First Startup**: On first startup with an empty database, a system administrator user is automatically created with the configured email address
- **Login Link**: The admin user can then request a login link to the configured email to gain initial access to the system

### When Writing Code
1. **Always use English** for code, comments, and documentation
2. **German only for UI** strings and user-facing content
3. **Think interface-first**: Design for future extensibility
4. **Security first**: Always consider GDPR and security implications
   - Never allow context emails to be used for login/authentication
   - Always enforce assignment-level authorization for recipient resolution
   - Keep visibility filtering and delivery resolution logically separate
5. **Async everywhere**: Use async/await with CancellationToken
6. **Log appropriately**: Use ILogger interface; Info for normal flow, Warning for issues, Error for exceptions
7. **Test as you go**: Write tests alongside features
8. **Keep it simple**: Start with simple implementations, refactor when needed

### When Suggesting Changes
- Consider impact on existing code
- Maintain backward compatibility where possible
- Suggest migrations for breaking changes
- Always consider security and privacy implications

---

**Last Updated**: April 5, 2026
**Version**: 2.1
