# Secure Application Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deployable foundation where a Firebase-authenticated user can create a private document, upload one supported file to quarantine, have it validated asynchronously, and see its status without exposing another user's data.

**Architecture:** Use a feature-oriented .NET modular monolith with separate API and worker hosts, PostgreSQL for structured state and job leasing, and an application-owned object-store interface backed by Cloudflare R2. Use an Angular 22 PWA with Firebase Authentication and App Check; all binary uploads bypass API memory and enter a private quarantine prefix before promotion.

**Tech Stack:** Angular 22, TypeScript, Node.js 22, ASP.NET Core 10, C# 14, EF Core 10, Npgsql, PostgreSQL, Firebase Admin SDK, Cloudflare R2 through the S3 API, xUnit, Testcontainers, Vitest, Playwright, Docker, and ClamAV.

**Spec:** `docs/superpowers/specs/2026-09-02-superscanner-phase-1-design.md`

## Global Constraints

- Target Angular 22 and `net10.0`; commit `package-lock.json` and NuGet lock files after the first successful restore.
- Keep document binaries out of PostgreSQL; store only private object keys, media metadata, hashes, and lifecycle state.
- Derive the user identifier from a verified Firebase token; never accept an owner ID from request JSON or route values.
- Require Firebase App Check on every authenticated API route, including signed-URL issuance and upload completion.
- Keep originals logically immutable; derived processing must never overwrite an accepted original object key.
- Place every upload in a quarantine prefix until size, declared type, magic bytes, SHA-256, and malware checks pass.
- Use PostgreSQL-backed jobs with transactional claims, leases, heartbeats, idempotency keys, and bounded retries; do not add Redis.
- Keep API, worker, PostgreSQL, and object-store/provider concerns behind focused interfaces.
- Exclude document pixels, OCR text, filenames, signed URLs, authentication tokens, and secrets from ordinary logs.
- Support English only, at most 50 pages per document by database-configurable policy, and no monetization or advertisements.
- Keep PostgreSQL and the worker private on Railway; expose only the web application and API.

---

## File Structure

```text
SuperScanner.slnx
global.json
Directory.Build.props
docker-compose.yml
.github/workflows/ci.yml
src/
  SuperScanner.Domain/
    Documents/Document.cs
    Documents/DocumentStatus.cs
    Documents/Page.cs
    Uploads/UploadIntent.cs
    Processing/ProcessingJob.cs
    Auditing/AuditEvent.cs
  SuperScanner.Application/
    Abstractions/IAuditWriter.cs
    Abstractions/IClock.cs
    Abstractions/IObjectStore.cs
    Abstractions/IRequestIdentityVerifier.cs
    Documents/CreateDocument.cs
    Documents/ListDocuments.cs
    Uploads/CreateUploadIntent.cs
    Uploads/CompleteUpload.cs
    Uploads/ValidateUpload.cs
  SuperScanner.Infrastructure/
    Persistence/AppDbContext.cs
    Persistence/Configurations/*.cs
    Persistence/Migrations/*
    Auth/FirebaseRequestIdentityVerifier.cs
    ObjectStorage/R2ObjectStore.cs
    Processing/PostgresJobQueue.cs
    Security/ClamAvMalwareScanner.cs
    Auditing/HmacAuditWriter.cs
  SuperScanner.Api/
    Auth/CurrentUser.cs
    Auth/FirebaseAuthenticationHandler.cs
    Auth/AppCheckMiddleware.cs
    Endpoints/DocumentsEndpoints.cs
    Endpoints/UploadsEndpoints.cs
    Program.cs
    Dockerfile
  SuperScanner.Worker/
    UploadValidationWorker.cs
    Program.cs
    Dockerfile
tests/
  SuperScanner.Domain.Tests/
  SuperScanner.Application.Tests/
  SuperScanner.Infrastructure.IntegrationTests/
  SuperScanner.Api.IntegrationTests/
apps/web/
  src/app/core/auth/*
  src/app/core/api/*
  src/app/layout/*
  src/app/documents/*
  e2e/foundation.spec.ts
  Dockerfile
```

The Domain project owns invariants and state transitions. Application owns use cases and provider-independent contracts. Infrastructure owns EF Core and external-service implementations. API and Worker are thin composition roots. Angular keeps authentication/API plumbing in `core`, application chrome in `layout`, and document behavior in `documents`.

---

### Task 1: Repository and Build Baseline

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `SuperScanner.slnx`
- Create: `src/SuperScanner.Domain/SuperScanner.Domain.csproj`
- Create: `src/SuperScanner.Application/SuperScanner.Application.csproj`
- Create: `src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj`
- Create: `src/SuperScanner.Api/SuperScanner.Api.csproj`
- Create: `src/SuperScanner.Worker/SuperScanner.Worker.csproj`
- Create: `tests/SuperScanner.Domain.Tests/SuperScanner.Domain.Tests.csproj`
- Create: `tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj`
- Create: `tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj`
- Create: `tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj`
- Create: `apps/web/*` from Angular CLI

**Interfaces:**
- Consumes: .NET SDK 10.0.400, Node.js 22, and npm 10 available on the development host.
- Produces: one solution whose .NET projects build and one Angular 22 application whose generated unit test passes.

- [ ] **Step 1: Pin the SDK and shared compiler rules**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.400",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest</AnalysisLevel>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Scaffold the solution and projects**

Run from `C:\yeow\SuperScanner`:

```powershell
dotnet new sln -n SuperScanner
dotnet new classlib -n SuperScanner.Domain -o src/SuperScanner.Domain
dotnet new classlib -n SuperScanner.Application -o src/SuperScanner.Application
dotnet new classlib -n SuperScanner.Infrastructure -o src/SuperScanner.Infrastructure
dotnet new webapi -n SuperScanner.Api -o src/SuperScanner.Api --use-controllers false
dotnet new worker -n SuperScanner.Worker -o src/SuperScanner.Worker
dotnet new xunit -n SuperScanner.Domain.Tests -o tests/SuperScanner.Domain.Tests
dotnet new xunit -n SuperScanner.Application.Tests -o tests/SuperScanner.Application.Tests
dotnet new xunit -n SuperScanner.Infrastructure.IntegrationTests -o tests/SuperScanner.Infrastructure.IntegrationTests
dotnet new xunit -n SuperScanner.Api.IntegrationTests -o tests/SuperScanner.Api.IntegrationTests
dotnet sln SuperScanner.slnx add (Get-ChildItem src,tests -Recurse -Filter *.csproj | Select-Object -ExpandProperty FullName)
dotnet add src/SuperScanner.Application reference src/SuperScanner.Domain
dotnet add src/SuperScanner.Infrastructure reference src/SuperScanner.Domain src/SuperScanner.Application
dotnet add src/SuperScanner.Api reference src/SuperScanner.Application src/SuperScanner.Infrastructure
dotnet add src/SuperScanner.Worker reference src/SuperScanner.Application src/SuperScanner.Infrastructure
dotnet add tests/SuperScanner.Domain.Tests reference src/SuperScanner.Domain
dotnet add tests/SuperScanner.Application.Tests reference src/SuperScanner.Application src/SuperScanner.Domain
dotnet add tests/SuperScanner.Infrastructure.IntegrationTests reference src/SuperScanner.Infrastructure
dotnet add tests/SuperScanner.Api.IntegrationTests reference src/SuperScanner.Api
```

- [ ] **Step 3: Scaffold Angular 22 with routing, SCSS, and Vitest**

Run:

```powershell
node "C:\Program Files\nodejs\node_modules\npm\bin\npx-cli.js" -p @angular/cli@22 ng new web --directory apps/web --routing --style=scss --standalone --strict --skip-git --package-manager npm
```

- [ ] **Step 4: Verify the clean baseline**

Run:

```powershell
dotnet build SuperScanner.slnx
node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web test -- --watch=false
node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web run build
```

Expected: all commands exit 0; no compiler warning is emitted.

- [ ] **Step 5: Commit**

```powershell
git add SuperScanner.slnx global.json Directory.Build.props src tests apps/web
git commit -m "build: scaffold SuperScanner applications"
```

---

### Task 2: Document Aggregate and Invariants

**Files:**
- Create: `src/SuperScanner.Domain/Documents/DocumentStatus.cs`
- Create: `src/SuperScanner.Domain/Documents/Document.cs`
- Create: `src/SuperScanner.Domain/Documents/Page.cs`
- Test: `tests/SuperScanner.Domain.Tests/Documents/DocumentTests.cs`

**Interfaces:**
- Consumes: `Guid`, Firebase UID as a non-empty `string`, and UTC `DateTimeOffset` values.
- Produces: `Document.Create(Guid, string, string, DateTimeOffset)`, `Document.AddPage(Guid, int, DateTimeOffset)`, `Document.MarkProcessing(DateTimeOffset)`, and `Document.MarkReady(DateTimeOffset)`.

- [ ] **Step 1: Write failing aggregate tests**

```csharp
using SuperScanner.Domain.Documents;

namespace SuperScanner.Domain.Tests.Documents;

public sealed class DocumentTests
{
    [Fact]
    public void Create_TrimsTitleAndOwnsDocument()
    {
        var now = DateTimeOffset.Parse("2026-09-02T00:00:00Z");
        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "  Application form  ", now);

        Assert.Equal("firebase-user-1", document.OwnerFirebaseUid);
        Assert.Equal("Application form", document.Title);
        Assert.Equal(DocumentStatus.Draft, document.Status);
        Assert.Equal(now, document.CreatedAt);
    }

    [Fact]
    public void AddPage_RejectsConfiguredLimit()
    {
        var document = Document.Create(Guid.NewGuid(), "firebase-user-1", "Form", DateTimeOffset.UtcNow);
        document.AddPage(Guid.NewGuid(), 1, DateTimeOffset.UtcNow);

        var error = Assert.Throws<InvalidOperationException>(() =>
            document.AddPage(Guid.NewGuid(), 1, DateTimeOffset.UtcNow));

        Assert.Equal("Document page limit of 1 reached.", error.Message);
    }
}
```

- [ ] **Step 2: Run the tests and confirm failure**

Run: `dotnet test tests/SuperScanner.Domain.Tests --filter FullyQualifiedName~DocumentTests`

Expected: FAIL because `SuperScanner.Domain.Documents.Document` does not exist.

- [ ] **Step 3: Implement the minimum aggregate**

```csharp
namespace SuperScanner.Domain.Documents;

public enum DocumentStatus { Draft, Uploading, Processing, Ready, Editing, Exporting, Completed, Failed }

public sealed class Document
{
    private readonly List<Page> _pages = [];
    private Document() { }

    public Guid Id { get; private set; }
    public string OwnerFirebaseUid { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public DocumentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<Page> Pages => _pages;

    public static Document Create(Guid id, string ownerFirebaseUid, string title, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerFirebaseUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return new Document { Id = id, OwnerFirebaseUid = ownerFirebaseUid, Title = title.Trim(), Status = DocumentStatus.Draft, CreatedAt = now, UpdatedAt = now };
    }

    public Page AddPage(Guid pageId, int maxPages, DateTimeOffset now)
    {
        if (maxPages < 1) throw new ArgumentOutOfRangeException(nameof(maxPages));
        if (_pages.Count >= maxPages) throw new InvalidOperationException($"Document page limit of {maxPages} reached.");
        var page = Page.Create(pageId, Id, _pages.Count + 1, now);
        _pages.Add(page);
        Status = DocumentStatus.Uploading;
        UpdatedAt = now;
        return page;
    }

    public void MarkProcessing(DateTimeOffset now) { Status = DocumentStatus.Processing; UpdatedAt = now; }
    public void MarkReady(DateTimeOffset now) { Status = DocumentStatus.Ready; UpdatedAt = now; }
}

public sealed class Page
{
    private Page() { }
    public Guid Id { get; private set; }
    public Guid DocumentId { get; private set; }
    public int PageNumber { get; private set; }
    public string? OriginalObjectKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    internal static Page Create(Guid id, Guid documentId, int pageNumber, DateTimeOffset now) =>
        new() { Id = id, DocumentId = documentId, PageNumber = pageNumber, CreatedAt = now };
    public void AcceptOriginal(string objectKey) => OriginalObjectKey = string.IsNullOrWhiteSpace(OriginalObjectKey) ? objectKey : throw new InvalidOperationException("Original asset is immutable.");
}
```

- [ ] **Step 4: Run all domain tests**

Run: `dotnet test tests/SuperScanner.Domain.Tests`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/SuperScanner.Domain tests/SuperScanner.Domain.Tests
git commit -m "feat: add document lifecycle aggregate"
```

---

### Task 3: PostgreSQL Persistence and Migration

**Files:**
- Create: `src/SuperScanner.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Configurations/DocumentConfiguration.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Configurations/PageConfiguration.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/DesignTimeDbContextFactory.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Migrations/*InitialSchema*`
- Test: `tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/DocumentPersistenceTests.cs`

**Interfaces:**
- Consumes: `Document` and `Page` from Task 2 plus a PostgreSQL connection string.
- Produces: `AppDbContext.Documents`, `AppDbContext.Pages`, migration `InitialSchema`, and owner-indexed document queries.

- [ ] **Step 1: Add EF Core, Npgsql, and Testcontainers packages**

```powershell
dotnet add src/SuperScanner.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/SuperScanner.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/SuperScanner.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add tests/SuperScanner.Infrastructure.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/SuperScanner.Infrastructure.IntegrationTests package Microsoft.EntityFrameworkCore.Relational
```

- [ ] **Step 2: Write a failing PostgreSQL round-trip test**

```csharp
public sealed class DocumentPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task SavesAndFiltersDocumentByOwner()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        db.Documents.Add(Document.Create(Guid.NewGuid(), "owner-a", "Form", DateTimeOffset.UtcNow));
        db.Documents.Add(Document.Create(Guid.NewGuid(), "owner-b", "Private", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var owned = await db.Documents.Where(x => x.OwnerFirebaseUid == "owner-a").ToListAsync();
        Assert.Single(owned);
        Assert.Equal("Form", owned[0].Title);
    }
}
```

- [ ] **Step 3: Run the test and confirm failure**

Run: `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests --filter FullyQualifiedName~DocumentPersistenceTests`

Expected: FAIL because `AppDbContext` does not exist.

- [ ] **Step 4: Implement context and focused entity configurations**

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Page> Pages => Set<Page>();
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OwnerFirebaseUid).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.HasIndex(x => new { x.OwnerFirebaseUid, x.UpdatedAt });
        builder.HasMany(x => x.Pages).WithOne().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Pages).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
```

- [ ] **Step 5: Create and apply the migration**

```powershell
dotnet ef migrations add InitialSchema --project src/SuperScanner.Infrastructure --startup-project src/SuperScanner.Api --output-dir Persistence/Migrations
dotnet test tests/SuperScanner.Infrastructure.IntegrationTests --filter FullyQualifiedName~DocumentPersistenceTests
```

Expected: PASS and migration files contain `documents` and `pages` tables.

- [ ] **Step 6: Commit**

```powershell
git add src/SuperScanner.Infrastructure tests/SuperScanner.Infrastructure.IntegrationTests
git commit -m "feat: persist private document metadata"
```

---

### Task 4: Firebase Authentication and App Check Boundary

**Files:**
- Create: `src/SuperScanner.Application/Abstractions/IRequestIdentityVerifier.cs`
- Create: `src/SuperScanner.Infrastructure/Auth/FirebaseRequestIdentityVerifier.cs`
- Create: `src/SuperScanner.Api/Auth/CurrentUser.cs`
- Create: `src/SuperScanner.Api/Auth/FirebaseAuthenticationHandler.cs`
- Create: `src/SuperScanner.Api/Auth/AppCheckMiddleware.cs`
- Modify: `src/SuperScanner.Api/Program.cs`
- Test: `tests/SuperScanner.Api.IntegrationTests/Auth/AuthenticationBoundaryTests.cs`

**Interfaces:**
- Consumes: `Authorization: Bearer <Firebase-ID-token>` and `X-Firebase-AppCheck: <token>` headers.
- Produces: `IRequestIdentityVerifier.VerifyAsync(string idToken, string appCheckToken, CancellationToken) -> VerifiedRequestIdentity`, and `ICurrentUser.FirebaseUid` for authorized endpoints.

- [ ] **Step 1: Define the provider-independent identity contract**

```csharp
public sealed record VerifiedRequestIdentity(string FirebaseUid, string? Email);

public interface IRequestIdentityVerifier
{
    Task<VerifiedRequestIdentity> VerifyAsync(string idToken, string appCheckToken, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write failing boundary tests with a fake verifier**

```csharp
[Fact]
public async Task Me_RejectsMissingAppCheckToken()
{
    using var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", "valid-user-a");
    var response = await client.GetAsync("/api/me");
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Fact]
public async Task Me_ReturnsVerifiedFirebaseUid()
{
    using var client = _factory.CreateClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", "valid-user-a");
    client.DefaultRequestHeaders.Add("X-Firebase-AppCheck", "valid-app");
    var body = await client.GetFromJsonAsync<MeResponse>("/api/me");
    Assert.Equal("user-a", body!.FirebaseUid);
}
```

- [ ] **Step 3: Run tests and confirm failure**

Run: `dotnet test tests/SuperScanner.Api.IntegrationTests --filter FullyQualifiedName~AuthenticationBoundaryTests`

Expected: FAIL because `/api/me` and the authentication boundary do not exist.

- [ ] **Step 4: Implement Firebase verification and the request boundary**

Add `FirebaseAdmin` to Infrastructure. `FirebaseRequestIdentityVerifier` calls `FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken, true, cancellationToken)` and `FirebaseAppCheck.DefaultInstance.VerifyTokenAsync(appCheckToken, cancellationToken)`, then returns the Firebase subject and email. Translate verification failures to a single unauthorized result without logging tokens.

Map the verification result to a `ClaimsPrincipal` containing `ClaimTypes.NameIdentifier`. Add middleware that rejects a missing or invalid App Check header before protected endpoints execute. Map `/api/me` as:

```csharp
app.MapGet("/api/me", (ICurrentUser currentUser) => Results.Ok(new { firebaseUid = currentUser.FirebaseUid }))
   .RequireAuthorization();
```

- [ ] **Step 5: Run authentication tests**

Run: `dotnet test tests/SuperScanner.Api.IntegrationTests --filter FullyQualifiedName~AuthenticationBoundaryTests`

Expected: PASS for valid paired tokens; missing, expired, malformed, or mismatched tokens return 401.

- [ ] **Step 6: Commit**

```powershell
git add src/SuperScanner.Application src/SuperScanner.Infrastructure/Auth src/SuperScanner.Api/Auth src/SuperScanner.Api/Program.cs tests/SuperScanner.Api.IntegrationTests/Auth
git commit -m "feat: enforce Firebase identity and app attestation"
```

---

### Task 5: Owner-Scoped Document API

**Files:**
- Create: `src/SuperScanner.Application/Abstractions/IClock.cs`
- Create: `src/SuperScanner.Application/Documents/CreateDocument.cs`
- Create: `src/SuperScanner.Application/Documents/ListDocuments.cs`
- Create: `src/SuperScanner.Api/Endpoints/DocumentsEndpoints.cs`
- Test: `tests/SuperScanner.Api.IntegrationTests/Documents/DocumentsEndpointsTests.cs`

**Interfaces:**
- Consumes: `ICurrentUser.FirebaseUid`, `CreateDocumentRequest(string Title)`, `AppDbContext`, and `IClock.UtcNow`.
- Produces: `POST /api/documents -> DocumentSummary` and `GET /api/documents -> IReadOnlyList<DocumentSummary>` filtered by verified owner.

- [ ] **Step 1: Write an isolation test**

```csharp
[Fact]
public async Task List_ReturnsOnlyCurrentUsersDocuments()
{
    var userA = _factory.CreateAuthenticatedClient("user-a");
    var userB = _factory.CreateAuthenticatedClient("user-b");
    await userA.PostAsJsonAsync("/api/documents", new { title = "A form" });
    await userB.PostAsJsonAsync("/api/documents", new { title = "B form" });

    var documents = await userA.GetFromJsonAsync<List<DocumentSummary>>("/api/documents");

    Assert.Single(documents!);
    Assert.Equal("A form", documents![0].Title);
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/SuperScanner.Api.IntegrationTests --filter FullyQualifiedName~DocumentsEndpointsTests`

Expected: FAIL with 404 because document endpoints are absent.

- [ ] **Step 3: Implement create and list handlers**

```csharp
public sealed record DocumentSummary(Guid Id, string Title, string Status, int PageCount, DateTimeOffset UpdatedAt);

public sealed class CreateDocument(AppDbContext db, IClock clock)
{
    public async Task<DocumentSummary> HandleAsync(string ownerUid, string title, CancellationToken ct)
    {
        var document = Document.Create(Guid.NewGuid(), ownerUid, title, clock.UtcNow);
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
        return new(document.Id, document.Title, document.Status.ToString(), 0, document.UpdatedAt);
    }
}

public sealed class ListDocuments(AppDbContext db)
{
    public Task<List<DocumentSummary>> HandleAsync(string ownerUid, CancellationToken ct) =>
        db.Documents.AsNoTracking()
          .Where(x => x.OwnerFirebaseUid == ownerUid)
          .OrderByDescending(x => x.UpdatedAt)
          .Select(x => new DocumentSummary(x.Id, x.Title, x.Status.ToString(), x.Pages.Count, x.UpdatedAt))
          .ToListAsync(ct);
}
```

Validate titles as 1-200 trimmed characters and return RFC 9457 problem details for invalid input. Map endpoints in a single `DocumentsEndpoints.Map(IEndpointRouteBuilder)` method.

- [ ] **Step 4: Run API tests**

Run: `dotnet test tests/SuperScanner.Api.IntegrationTests --filter FullyQualifiedName~DocumentsEndpointsTests`

Expected: PASS, including user isolation and invalid-title cases.

- [ ] **Step 5: Commit**

```powershell
git add src/SuperScanner.Application src/SuperScanner.Api/Endpoints tests/SuperScanner.Api.IntegrationTests/Documents
git commit -m "feat: add owner-scoped document API"
```

---

### Task 6: Quarantined Direct Upload Intents

**Files:**
- Create: `src/SuperScanner.Domain/Uploads/UploadIntent.cs`
- Create: `src/SuperScanner.Application/Abstractions/IObjectStore.cs`
- Create: `src/SuperScanner.Application/Uploads/CreateUploadIntent.cs`
- Create: `src/SuperScanner.Application/Uploads/CompleteUpload.cs`
- Create: `src/SuperScanner.Infrastructure/ObjectStorage/R2ObjectStore.cs`
- Create: `src/SuperScanner.Api/Endpoints/UploadsEndpoints.cs`
- Test: `tests/SuperScanner.Application.Tests/Uploads/CreateUploadIntentTests.cs`
- Test: `tests/SuperScanner.Api.IntegrationTests/Uploads/UploadOwnershipTests.cs`

**Interfaces:**
- Consumes: owned `Document`, `CreateUploadRequest(FileName, MediaType, SizeBytes, Sha256Hex)`, policy limits, and `IObjectStore`.
- Produces: `UploadIntentDto(Guid UploadId, Guid PageId, Uri PutUrl, DateTimeOffset ExpiresAt)`, `IObjectStore.CreatePutUrlAsync`, and completion that queues validation without accepting the object as an original.

- [ ] **Step 1: Write failing upload-intent tests**

```csharp
[Fact]
public async Task Create_UsesOpaqueQuarantineKeyAndNeverUserFilename()
{
    var store = new RecordingObjectStore();
    var handler = new CreateUploadIntent(_db, store, _clock, new UploadPolicy(50, 25 * 1024 * 1024));
    var result = await handler.HandleAsync("user-a", _document.Id,
        new("tax-form.pdf", "application/pdf", 1200, new string('a', 64)), CancellationToken.None);

    Assert.StartsWith($"quarantine/{_document.Id:N}/", store.LastObjectKey, StringComparison.Ordinal);
    Assert.DoesNotContain("tax-form", store.LastObjectKey, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(_clock.UtcNow.AddMinutes(5), result.ExpiresAt);
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/SuperScanner.Application.Tests --filter FullyQualifiedName~CreateUploadIntentTests`

Expected: FAIL because upload contracts do not exist.

- [ ] **Step 3: Implement the upload contract and intent state**

```csharp
public sealed record PutObjectRequest(string ObjectKey, string MediaType, long SizeBytes, DateTimeOffset ExpiresAt);
public sealed record StoredObjectInfo(long SizeBytes, string MediaType, string ETag);

public interface IObjectStore
{
    Task<Uri> CreatePutUrlAsync(PutObjectRequest request, CancellationToken cancellationToken);
    Task<StoredObjectInfo?> HeadAsync(string objectKey, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string objectKey, CancellationToken cancellationToken);
    Task PromoteAsync(string quarantineKey, string acceptedKey, CancellationToken cancellationToken);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}
```

`UploadIntent` stores owner UID, document/page IDs, opaque quarantine key, declared media type/size/hash, expiry, state, and idempotency key. Reject expired intents, unsupported types, non-lowercase 64-character SHA-256, files over 25 MiB, documents not owned by the caller, and documents at the configured page limit.

- [ ] **Step 4: Implement R2 presigning**

Configure `AmazonS3Client` with the R2 endpoint, path-style access, and server-side credentials. Presign a five-minute PUT for the exact key and content type. Do not log the URL or credentials.

- [ ] **Step 5: Implement completion as an idempotent state transition**

`POST /api/documents/{documentId}/uploads/{uploadId}/complete` checks ownership and expiry, HEADs the quarantine object, compares declared size, marks the intent `PendingValidation`, and enqueues one `ValidateUpload` job using `upload:{uploadId}:validate` as the unique idempotency key. A repeat completion returns the existing state.

- [ ] **Step 6: Run upload tests**

```powershell
dotnet test tests/SuperScanner.Application.Tests --filter FullyQualifiedName~CreateUploadIntentTests
dotnet test tests/SuperScanner.Api.IntegrationTests --filter FullyQualifiedName~UploadOwnershipTests
```

Expected: PASS; another user receives 404 rather than learning whether a document or upload exists.

- [ ] **Step 7: Commit**

```powershell
git add src/SuperScanner.Domain/Uploads src/SuperScanner.Application/Uploads src/SuperScanner.Application/Abstractions/IObjectStore.cs src/SuperScanner.Infrastructure/ObjectStorage src/SuperScanner.Api/Endpoints/UploadsEndpoints.cs tests
git commit -m "feat: issue private quarantined upload intents"
```

---

### Task 7: PostgreSQL Job Leasing and Upload Validation Worker

**Files:**
- Create: `src/SuperScanner.Domain/Processing/ProcessingJob.cs`
- Create: `src/SuperScanner.Application/Abstractions/IMalwareScanner.cs`
- Create: `src/SuperScanner.Application/Uploads/UploadFileSignature.cs`
- Create: `src/SuperScanner.Application/Uploads/ValidateUpload.cs`
- Create: `src/SuperScanner.Infrastructure/Processing/PostgresJobQueue.cs`
- Create: `src/SuperScanner.Infrastructure/Security/ClamAvMalwareScanner.cs`
- Create: `src/SuperScanner.Worker/UploadValidationWorker.cs`
- Modify: `src/SuperScanner.Worker/Program.cs`
- Test: `tests/SuperScanner.Application.Tests/Uploads/ValidateUploadTests.cs`
- Test: `tests/SuperScanner.Infrastructure.IntegrationTests/Processing/JobLeaseTests.cs`

**Interfaces:**
- Consumes: `ProcessingJob`, `IObjectStore`, `IMalwareScanner.ScanAsync(Stream, CancellationToken)`, declared upload metadata, and a UTC clock.
- Produces: atomic `PostgresJobQueue.TryLeaseAsync(workerId, leaseDuration, ct)`, heartbeats, bounded retry state, accepted object key `originals/{documentId:N}/{pageId:N}/{sha256}`, or safe rejection with quarantine deletion.

- [ ] **Step 1: Write concurrent lease and validation tests**

```csharp
[Fact]
public async Task OnlyOneWorkerLeasesAJob()
{
    await _queue.EnqueueAsync("ValidateUpload", "upload-1", "upload:1:validate", CancellationToken.None);
    var claims = await Task.WhenAll(
        _queue.TryLeaseAsync("worker-a", TimeSpan.FromMinutes(2), CancellationToken.None),
        _queue.TryLeaseAsync("worker-b", TimeSpan.FromMinutes(2), CancellationToken.None));
    Assert.Single(claims.Where(x => x is not null));
}

[Fact]
public async Task InfectedUploadIsDeletedAndNeverAccepted()
{
    var scanner = new FakeMalwareScanner(MalwareScanResult.Infected("Eicar-Test-Signature"));
    var result = await _handler.ValidateAsync(_uploadId, scanner, CancellationToken.None);
    Assert.Equal(UploadValidationOutcome.Rejected, result.Outcome);
    Assert.Null(_page.OriginalObjectKey);
    Assert.Contains(_quarantineKey, _store.DeletedKeys);
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/SuperScanner.Application.Tests tests/SuperScanner.Infrastructure.IntegrationTests --filter "FullyQualifiedName~ValidateUploadTests|FullyQualifiedName~JobLeaseTests"`

Expected: FAIL because validation and leasing do not exist.

- [ ] **Step 3: Implement transactional leasing**

Use one PostgreSQL transaction and `FOR UPDATE SKIP LOCKED` to select the oldest queued or expired-leased job. Update worker ID, lease expiry, attempt count, and status before commit. Add a unique index on idempotency key. Mark a job permanently failed after five attempts; transient retries use 5 seconds, 30 seconds, 2 minutes, 10 minutes, and 30 minutes.

- [ ] **Step 4: Implement deterministic signature validation**

```csharp
public static string DetectMediaType(ReadOnlySpan<byte> header)
{
    if (header.StartsWith([0x25, 0x50, 0x44, 0x46, 0x2D])) return "application/pdf";
    if (header.StartsWith([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return "image/png";
    if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return "image/jpeg";
    if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8) &&
        (header[8..12].SequenceEqual("heic"u8) || header[8..12].SequenceEqual("heix"u8))) return "image/heic";
    throw new InvalidDataException("Unsupported or mismatched file signature.");
}
```

Stream once into a bounded temporary file while computing SHA-256 and enforcing the maximum byte count. Compare actual size, detected media type, and hash with the upload intent before malware scanning.

- [ ] **Step 5: Implement fail-closed malware scanning and promotion**

`ClamAvMalwareScanner` sends the bounded stream to a private `clamd` service. Clean results permit copy/promotion to the content-addressed original key; infected, scanner-error, mismatched, oversized, or expired results never set `Page.OriginalObjectKey`. Scanner unavailability is transient and keeps the item quarantined for bounded retry.

- [ ] **Step 6: Implement the worker loop**

`UploadValidationWorker` leases one job, starts a periodic heartbeat at one-third of the lease duration, dispatches `ValidateUpload`, records a safe error code, and completes or reschedules the job. Logs contain job/upload IDs and error codes only.

- [ ] **Step 7: Run worker and queue tests**

Run: `dotnet test tests/SuperScanner.Application.Tests tests/SuperScanner.Infrastructure.IntegrationTests`

Expected: PASS, including clean promotion, infected deletion, signature mismatch, hash mismatch, scanner outage retry, duplicate enqueue, and concurrent claim cases.

- [ ] **Step 8: Commit**

```powershell
git add src/SuperScanner.Domain/Processing src/SuperScanner.Application/Uploads src/SuperScanner.Infrastructure/Processing src/SuperScanner.Infrastructure/Security src/SuperScanner.Worker tests
git commit -m "feat: validate uploads in a leased background worker"
```

---

### Task 8: Tamper-Evident Audit Chain

**Files:**
- Create: `src/SuperScanner.Domain/Auditing/AuditEvent.cs`
- Create: `src/SuperScanner.Application/Abstractions/IAuditWriter.cs`
- Create: `src/SuperScanner.Infrastructure/Auditing/CanonicalAuditPayload.cs`
- Create: `src/SuperScanner.Infrastructure/Auditing/HmacAuditWriter.cs`
- Modify: document, upload-intent, completion, validation, and rejection handlers
- Test: `tests/SuperScanner.Infrastructure.IntegrationTests/Auditing/AuditChainTests.cs`

**Interfaces:**
- Consumes: `AuditWriteRequest(ActorUid, Action, TargetType, TargetId, RegionJson, OccurredAt)` and a protected HMAC key.
- Produces: insert-only `AuditEvent` rows containing previous hash, event hash, and signature; `IAuditWriter.AppendAsync` returns the event ID.

- [ ] **Step 1: Write a tamper-detection test**

```csharp
[Fact]
public async Task VerificationFailsAfterStoredEventMutation()
{
    await _writer.AppendAsync(new("user-a", "document.created", "document", _documentId, "{}", _now), CancellationToken.None);
    await _writer.AppendAsync(new("user-a", "upload.accepted", "document", _documentId, "{\"page\":1}", _now.AddSeconds(1)), CancellationToken.None);
    await _fixture.AlterSecondEventActionAsync("upload.replaced");

    var result = await _verifier.VerifyDocumentChainAsync(_documentId, CancellationToken.None);

    Assert.False(result.IsValid);
    Assert.Equal(2, result.FirstInvalidSequence);
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests --filter FullyQualifiedName~AuditChainTests`

Expected: FAIL because audit persistence and verification do not exist.

- [ ] **Step 3: Implement canonical hashing and append-only writes**

Canonicalize a payload with fixed property order and invariant UTC timestamps. Compute:

```text
eventHash = SHA256(previousHash || UTF8(canonicalPayload))
signature = HMACSHA256(auditSigningKey, eventHash)
```

Lock the current document chain tail during append, increment sequence, and insert one row. Grant the runtime database role `SELECT` and `INSERT` but not `UPDATE` or `DELETE` on `audit_events`.

- [ ] **Step 4: Add audit calls to completed use cases**

Record `document.created`, `upload.intent_created`, `upload.completed`, `upload.accepted`, and `upload.rejected`. Store region metadata and identifiers only; exclude titles, filenames, signed URLs, hashes supplied as secrets, tokens, and file contents.

- [ ] **Step 5: Run audit and API regression tests**

Run: `dotnet test SuperScanner.slnx`

Expected: PASS and the isolation tests still demonstrate no cross-owner access.

- [ ] **Step 6: Commit**

```powershell
git add src/SuperScanner.Domain/Auditing src/SuperScanner.Application/Abstractions/IAuditWriter.cs src/SuperScanner.Infrastructure/Auditing src/SuperScanner.Application tests
git commit -m "feat: record tamper-evident document audit events"
```

---

### Task 9: Angular Authentication, App Check, and Application Shell

**Files:**
- Create: `apps/web/src/environments/environment.ts`
- Create: `apps/web/src/app/core/auth/firebase.providers.ts`
- Create: `apps/web/src/app/core/auth/auth.service.ts`
- Create: `apps/web/src/app/core/auth/auth.guard.ts`
- Create: `apps/web/src/app/core/api/security.interceptor.ts`
- Create: `apps/web/src/app/layout/app-shell.component.{ts,html,scss}`
- Create: `apps/web/src/app/documents/document-list.component.{ts,html,scss,spec.ts}`
- Modify: `apps/web/src/app/app.config.ts`
- Modify: `apps/web/src/app/app.routes.ts`
- Test: `apps/web/src/app/core/api/security.interceptor.spec.ts`

**Interfaces:**
- Consumes: Firebase web configuration injected at build/runtime, `Auth`, `AppCheck`, and API base URL.
- Produces: `AuthService.user$`, route guard, interceptor adding `Authorization` and `X-Firebase-AppCheck`, responsive application shell, and typed `GET /api/documents` dashboard.

- [ ] **Step 1: Install Firebase and write a failing interceptor test**

```powershell
node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web install firebase
```

```typescript
it('adds Firebase identity and App Check headers', async () => {
  identity.getIdToken.mockResolvedValue('id-token');
  attestation.getToken.mockResolvedValue('app-token');
  http.get('/api/documents').subscribe();
  const request = httpTesting.expectOne('/api/documents');
  expect(request.request.headers.get('Authorization')).toBe('Bearer id-token');
  expect(request.request.headers.get('X-Firebase-AppCheck')).toBe('app-token');
});
```

- [ ] **Step 2: Run and confirm failure**

Run: `node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web test -- --watch=false`

Expected: FAIL because security providers and interceptor do not exist.

- [ ] **Step 3: Implement modular Firebase providers and interceptor**

Initialize Firebase once, provide `Auth`, initialize App Check with reCAPTCHA Enterprise, and expose narrow wrappers:

```typescript
export interface IdentityTokenSource { getIdToken(): Promise<string>; }
export interface AppCheckTokenSource { getToken(): Promise<string>; }

export const securityInterceptor: HttpInterceptorFn = (request, next) => {
  const identity = inject(IDENTITY_TOKEN_SOURCE);
  const attestation = inject(APP_CHECK_TOKEN_SOURCE);
  return from(Promise.all([identity.getIdToken(), attestation.getToken()])).pipe(
    switchMap(([idToken, appToken]) => next(request.clone({ setHeaders: {
      Authorization: `Bearer ${idToken}`,
      'X-Firebase-AppCheck': appToken
    }})))
  );
};
```

Never place tokens in URLs, local storage, logs, errors, or analytics.

- [ ] **Step 4: Implement the responsive shell and document list**

Recreate the reviewed product hierarchy: SuperScanner brand, Home/Scan/Edit/Export navigation, private-by-default indicator, primary New Scan action, and recent-document list. At 320 px, collapse navigation labels without hiding the brand icon or user avatar. Use semantic buttons, landmarks, visible focus, 44 px coarse-pointer targets, and an `aria-live` region for loading/failure status.

- [ ] **Step 5: Add document-list tests**

Test loading, empty, error, and populated states. Verify document titles render as text, no HTML is trusted, New Scan is keyboard accessible, and API errors do not display raw provider messages.

- [ ] **Step 6: Run frontend checks**

```powershell
node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web test -- --watch=false
node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web run build
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add apps/web
git commit -m "feat: add secure Angular application shell"
```

---

### Task 10: Create-and-Upload User Flow

**Files:**
- Create: `apps/web/src/app/documents/documents-api.service.ts`
- Create: `apps/web/src/app/documents/new-document.component.{ts,html,scss,spec.ts}`
- Create: `apps/web/src/app/documents/upload.service.ts`
- Create: `apps/web/src/app/documents/upload-status.component.{ts,html,scss,spec.ts}`
- Modify: `apps/web/src/app/app.routes.ts`
- Test: `apps/web/src/app/documents/upload.service.spec.ts`

**Interfaces:**
- Consumes: document endpoints from Task 5 and upload-intent/completion endpoints from Task 6.
- Produces: user flow `create document -> request intent -> PUT bytes to R2 -> complete -> poll status`, with client SHA-256 and typed progress states.

- [ ] **Step 1: Write a failing upload orchestration test**

```typescript
it('creates, uploads, and completes in order', async () => {
  api.createDocument.mockResolvedValue({ id: 'doc-1', title: 'Form', status: 'Draft', pageCount: 0 });
  api.createUploadIntent.mockResolvedValue({ uploadId: 'up-1', pageId: 'page-1', putUrl: 'https://upload.invalid/object', expiresAt: '2026-09-02T01:00:00Z' });
  uploader.put.mockResolvedValue(undefined);
  api.completeUpload.mockResolvedValue({ status: 'PendingValidation' });

  await service.upload('Form', new File(['%PDF-1.7'], 'form.pdf', { type: 'application/pdf' }));

  expect(uploader.put).toHaveBeenCalledAfter(api.createUploadIntent);
  expect(api.completeUpload).toHaveBeenCalledAfter(uploader.put);
});
```

- [ ] **Step 2: Run and confirm failure**

Run: `node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web test -- --watch=false`

Expected: FAIL because upload services do not exist.

- [ ] **Step 3: Implement typed API and streaming SHA-256 preparation**

Define exact request/response types matching the API. Compute SHA-256 through `crypto.subtle.digest('SHA-256', await file.arrayBuffer())` for the Phase 1 25 MiB cap and encode lowercase hexadecimal. Reject unsupported MIME types and oversized files before requesting an upload intent.

- [ ] **Step 4: Implement direct PUT without credentials**

Use a dedicated `HttpClient` request to the returned URL with exactly the signed `Content-Type`; do not attach Firebase or App Check headers to the R2 request. Emit progress locally, discard the signed URL after completion, then call the authenticated completion endpoint.

- [ ] **Step 5: Implement accessible UI states**

The New Scan upload path shows selected file, size, validation error, upload percentage, `Validating securely`, accepted status, or a safe rejection reason. Disable duplicate submission while active. Closing and reopening the route reloads status from the API rather than trusting local state.

- [ ] **Step 6: Run frontend tests and build**

```powershell
node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web test -- --watch=false
node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web run build
```

Expected: PASS, including failed PUT, expired intent, duplicate click, rejected validation, and keyboard flow cases.

- [ ] **Step 7: Commit**

```powershell
git add apps/web/src/app/documents apps/web/src/app/app.routes.ts
git commit -m "feat: add quarantined document upload flow"
```

---

### Task 11: Local Stack, CI, Railway Containers, and End-to-End Gate

**Files:**
- Create: `docker-compose.yml`
- Create: `.env.example`
- Create: `src/SuperScanner.Api/Dockerfile`
- Create: `src/SuperScanner.Worker/Dockerfile`
- Create: `apps/web/Dockerfile`
- Create: `apps/web/nginx.conf`
- Create: `.github/workflows/ci.yml`
- Create: `apps/web/playwright.config.ts`
- Create: `apps/web/e2e/foundation.spec.ts`
- Create: `docs/operations/foundation-runbook.md`

**Interfaces:**
- Consumes: API, Worker, Angular app, PostgreSQL, R2-compatible MinIO for local development, ClamAV, and fake Firebase verification that is registered only in the `E2E` environment.
- Produces: one local command, three production containers, CI gates, and an end-to-end proof of owner isolation and validated upload.

- [ ] **Step 1: Write the end-to-end test before wiring the stack**

```typescript
test('user uploads a private document and sees validation complete', async ({ page }) => {
  await page.goto('/e2e-login?user=user-a');
  await page.getByRole('button', { name: 'New scan' }).click();
  await page.getByLabel('Document title').fill('Application form');
  await page.getByLabel('Choose file').setInputFiles('e2e/fixtures/clean-form.pdf');
  await page.getByRole('button', { name: 'Upload securely' }).click();
  await expect(page.getByText('Ready')).toBeVisible();
  await page.goto('/e2e-login?user=user-b');
  await expect(page.getByText('Application form')).not.toBeVisible();
});
```

- [ ] **Step 2: Run and confirm failure**

Run from `apps/web`: `node "C:\Program Files\nodejs\node_modules\npm\bin\npx-cli.js" playwright test e2e/foundation.spec.ts`

Expected: FAIL because the local integrated stack and E2E identity fixture are not configured.

The E2E identity endpoint and fake verifier must be registered only when `IHostEnvironment.EnvironmentName == "E2E"`. Add a startup test proving that the route is absent and the fake verifier cannot resolve in Development, Staging, and Production. Fail startup if an E2E-auth configuration key is present outside the E2E environment.

- [ ] **Step 3: Create the local dependency stack**

`docker-compose.yml` defines PostgreSQL 17, MinIO, and ClamAV on a private Docker network, with named volumes and health checks. API and Worker run from the host during development. `.env.example` contains names only, never usable credentials. Use explicit local-only credentials and document rotation/removal before any shared environment.

- [ ] **Step 4: Create least-privilege production containers**

Use multi-stage builds. API and Worker final images run as non-root, contain no SDK or source, expose only the required port, write temporary data only under a bounded writable temp directory, and include health endpoints. The Angular image serves immutable static assets with Nginx security headers; `/api` is configured by deployment environment rather than embedded secrets.

- [ ] **Step 5: Add CI gates**

The workflow runs on pull requests and pushes:

```yaml
jobs:
  backend:
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet restore SuperScanner.slnx --locked-mode
      - run: dotnet build SuperScanner.slnx --no-restore
      - run: dotnet test SuperScanner.slnx --no-build
  frontend:
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with: { node-version: '22', cache: 'npm', cache-dependency-path: 'apps/web/package-lock.json' }
      - run: npm ci
        working-directory: apps/web
      - run: npm test -- --watch=false
        working-directory: apps/web
      - run: npm run build
        working-directory: apps/web
```

Add container builds and a Playwright job after unit/integration jobs pass.

- [ ] **Step 6: Run the complete local verification**

```powershell
docker compose up -d --wait
dotnet test SuperScanner.slnx
node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web test -- --watch=false
node "C:\Program Files\nodejs\node_modules\npm\bin\npm-cli.js" --prefix apps/web run build
Push-Location apps/web
node "C:\Program Files\nodejs\node_modules\npm\bin\npx-cli.js" playwright test
Pop-Location
docker compose config --quiet
```

Expected: every command exits 0; the E2E test proves user B cannot see user A's document.

- [ ] **Step 7: Document Railway topology and incident controls**

The runbook specifies public web/API services, private Worker/PostgreSQL services, R2/Firebase/ClamAV variables, health checks, migration execution, secret rotation, upload quarantine inspection, worker pause/resume, share/provider disable switches, and rollback. It explicitly forbids public domains for Worker and PostgreSQL.

- [ ] **Step 8: Commit**

```powershell
git add docker-compose.yml .env.example .github src/SuperScanner.Api/Dockerfile src/SuperScanner.Worker/Dockerfile apps/web/Dockerfile apps/web/nginx.conf apps/web/playwright.config.ts apps/web/e2e docs/operations
git commit -m "chore: add secure foundation delivery pipeline"
```

---

## Completion Gate

The secure-foundation plan is complete only when:

1. A clean checkout restores from lock files and passes backend, frontend, container, and E2E commands.
2. Valid Firebase identity plus App Check is required for every protected route.
3. User A cannot list, complete, poll, or infer User B's document or upload IDs.
4. A file reaches an immutable original key only after declared metadata, signature, size, SHA-256, and malware checks pass.
5. Duplicate upload completion and worker retry cannot create duplicate pages, jobs, originals, or audit events.
6. Logs and test output contain no tokens, signed URLs, filenames, document bytes, or document text.
7. PostgreSQL and Worker have no public Railway domain in the documented topology.
8. The document dashboard and upload flow remain usable at 320 px and with keyboard-only navigation.

Once this gate passes, execute `docs/superpowers/plans/2026-09-02-phase-1-delivery-roadmap.md` Plan 2.
