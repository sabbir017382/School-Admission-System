using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:5173")
     .AllowAnyHeader().AllowAnyMethod()));

var connection = builder.Configuration.GetConnectionString("Default")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Port=5432;Database=school_admission;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(connection));
builder.Services.AddScoped<AdmissionService>();

var app = builder.Build();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

// For this take-home project, EnsureCreated keeps first startup simple.
// For production, replace this with EF Core migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    var retries = 0;
    while (true)
    {
        try { await db.Database.EnsureCreatedAsync(); break; }
        catch when (++retries < 20) { await Task.Delay(1500); }
    }
}
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.Run();

public partial class Program { }

public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<AdmissionClass> Classes => Set<AdmissionClass>();
    public DbSet<AdmissionHold> Holds => Set<AdmissionHold>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AdmissionClass>(e =>
        {
            e.ToTable("admission_classes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.TotalSeats).IsRequired();
        });
        b.Entity<AdmissionHold>(e =>
        {
            e.ToTable("admission_holds");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(40);
            e.Property(x => x.ClassId).HasMaxLength(40);
            e.Property(x => x.StudentId).HasMaxLength(30).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired();
            e.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId);
            e.HasIndex(x => new { x.ClassId, x.StudentId, x.Status });
            e.HasIndex(x => new { x.ClassId, x.Status, x.ExpiresAt });
        });
    }
}

public sealed class AdmissionClass
{
    public string Id { get; set; } = "c_" + Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public int TotalSeats { get; set; }
}

public sealed class AdmissionHold
{
    public string Id { get; set; } = "h_" + Guid.NewGuid().ToString("N");
    public string ClassId { get; set; } = "";
    public AdmissionClass? Class { get; set; }
    public string StudentId { get; set; } = "";
    public string Status { get; set; } = "HELD";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}

public sealed record CreateClassRequest(string Name, int TotalSeats);
public sealed record ClassDto(string Id, string Name, int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats);
public sealed record HoldDto(string HoldId, string ClassId, string StudentId, string Status, DateTimeOffset ExpiresAt);
public sealed record HoldListDto(string HoldId, string ClassId, string ClassName, string Status, DateTimeOffset ExpiresAt);
public sealed record ApiError(string Error);

public sealed class AdmissionService(AppDb db, IConfiguration config)
{
    private int HoldSeconds => Math.Clamp(
        int.TryParse(config["HOLD_DURATION_SECONDS"], out var n) ? n : 600, 30, 86400);

    public async Task<int> ExpireAsync(string classId, DateTimeOffset now, CancellationToken ct)
    {
        var expired = await db.Holds
            .Where(h => h.ClassId == classId && h.Status == "HELD" && h.ExpiresAt <= now)
            .ToListAsync(ct);
        foreach (var h in expired) h.Status = "EXPIRED";
        return expired.Count;
    }

    public async Task<ClassDto?> GetClassAsync(string id, CancellationToken ct)
    {
        var c = await db.Classes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        var now = DateTimeOffset.UtcNow;
        var held = await db.Holds.CountAsync(h => h.ClassId == id && h.Status == "HELD" && h.ExpiresAt > now, ct);
        var confirmed = await db.Holds.CountAsync(h => h.ClassId == id && h.Status == "CONFIRMED", ct);
        return new(c.Id, c.Name, c.TotalSeats, held, confirmed, Math.Max(0, c.TotalSeats - held - confirmed));
    }

    public async Task<(HoldDto? Hold, string? Error, int Code)> CreateHoldAsync(
        string classId, string studentId, CancellationToken ct)
    {
        if (!ValidStudentId(studentId)) return (null, "X-Student-Id must contain 17 digits.", 400);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // The class row is the mutex: all allocations for this class serialize here.
        var c = await db.Classes.FromSqlInterpolated(
            $"SELECT * FROM admission_classes WHERE \"Id\" = {classId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
        if (c is null) return (null, "Class not found.", 404);

        var now = DateTimeOffset.UtcNow;
        await ExpireAsync(classId, now, ct);

        var existing = await db.Holds.FirstOrDefaultAsync(h =>
            h.ClassId == classId && h.StudentId == studentId && h.Status == "HELD" && h.ExpiresAt > now, ct);
        if (existing is not null)
            return (new(existing.Id, existing.ClassId, existing.StudentId, existing.Status, existing.ExpiresAt), null, 200);

        var held = await db.Holds.CountAsync(h => h.ClassId == classId && h.Status == "HELD" && h.ExpiresAt > now, ct);
        var confirmed = await db.Holds.CountAsync(h => h.ClassId == classId && h.Status == "CONFIRMED", ct);
        if (held + confirmed >= c.TotalSeats)
            return (null, "No seats are currently available.", 409);

        var hold = new AdmissionHold {
            ClassId = classId, StudentId = studentId, Status = "HELD",
            CreatedAt = now, ExpiresAt = now.AddSeconds(HoldSeconds)
        };
        db.Holds.Add(hold);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (new(hold.Id, hold.ClassId, hold.StudentId, hold.Status, hold.ExpiresAt), null, 201);
    }

    public async Task<(string? Status, string? Error, int Code)> ConfirmAsync(
        string holdId, string studentId, CancellationToken ct)
    {
        if (!ValidStudentId(studentId)) return (null, "Invalid X-Student-Id.", 400);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var holdSnapshot = await db.Holds.AsNoTracking().FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (holdSnapshot is null) return (null, "Hold not found.", 404);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM admission_classes WHERE \"Id\" = {holdSnapshot.ClassId} FOR UPDATE", ct);
        var hold = await db.Holds.FirstOrDefaultAsync(h => h.Id == holdId, ct);
        if (hold is null) return (null, "Hold not found.", 404);
        if (hold.StudentId != studentId) return (null, "This hold belongs to another student.", 403);
        if (hold.Status == "CONFIRMED") { await tx.CommitAsync(ct); return ("CONFIRMED", null, 200); }
        if (hold.Status != "HELD") return (null, $"Cannot confirm a {hold.Status} hold.", 409);

        var now = DateTimeOffset.UtcNow;
        if (hold.ExpiresAt <= now)
        {
            hold.Status = "EXPIRED";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (null, "The hold has expired.", 409);
        }

        hold.Status = "CONFIRMED";
        hold.ConfirmedAt = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ("CONFIRMED", null, 200);
    }

    public static bool ValidStudentId(string? value) =>
        value is { Length: 17 } && value.All(char.IsDigit);
}

[ApiController]
public sealed class ClassesController(AppDb db, AdmissionService service) : ControllerBase
{
    [HttpPost("/classes")]
    public async Task<IActionResult> Create(CreateClassRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 150 || request.TotalSeats <= 0)
            return BadRequest(new ApiError("Name and positive totalSeats are required."));
        var name = request.Name.Trim();
        if (await db.Classes.AnyAsync(x => x.Name == name, ct))
            return Conflict(new ApiError("A class with this name already exists."));
        var c = new AdmissionClass { Name = name, TotalSeats = request.TotalSeats };
        db.Classes.Add(c);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new ApiError("A class with this name already exists.")); }
        return StatusCode(201, new { id = c.Id, name = c.Name, totalSeats = c.TotalSeats });
    }

    [HttpGet("/classes")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var ids = await db.Classes.AsNoTracking().Select(c => c.Id).ToListAsync(ct);
        var result = new List<ClassDto>();
        foreach (var id in ids) { var dto = await service.GetClassAsync(id, ct); if (dto is not null) result.Add(dto); }
        return Ok(result);
    }

    [HttpGet("/classes/{classId}")]
    public async Task<IActionResult> Get(string classId, CancellationToken ct)
    {
        var dto = await service.GetClassAsync(classId, ct);
        return dto is null ? NotFound(new ApiError("Class not found.")) : Ok(dto);
    }

    [HttpPost("/classes/{classId}/holds")]
    public async Task<IActionResult> Hold(string classId, [FromHeader(Name="X-Student-Id")] string studentId, CancellationToken ct)
    {
        var result = await service.CreateHoldAsync(classId, studentId, ct);
        if (result.Error is not null) return StatusCode(result.Code, new ApiError(result.Error));
        return StatusCode(result.Code, result.Hold);
    }
}

[ApiController]
public sealed class HoldsController(AppDb db, AdmissionService service) : ControllerBase
{
    [HttpGet("/holds/{holdId}")]
    public async Task<IActionResult> Get(string holdId, CancellationToken ct)
    {
        var h = await db.Holds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == holdId, ct);
        if (h is null) return NotFound(new ApiError("Hold not found."));
        if (h.Status == "HELD" && h.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            h = await db.Holds.FirstAsync(x => x.Id == holdId, ct);
            h.Status = "EXPIRED"; await db.SaveChangesAsync(ct);
        }
        return Ok(new HoldDto(h.Id, h.ClassId, h.StudentId, h.Status, h.ExpiresAt));
    }

    [HttpPost("/holds/{holdId}/confirm")]
    public async Task<IActionResult> Confirm(string holdId, [FromHeader(Name="X-Student-Id")] string studentId, CancellationToken ct)
    {
        var result = await service.ConfirmAsync(holdId, studentId, ct);
        if (result.Error is not null) return StatusCode(result.Code, new ApiError(result.Error));
        return Ok(new { holdId, status = result.Status });
    }

    [HttpDelete("/holds/{holdId}")]
    public async Task<IActionResult> Cancel(string holdId, [FromHeader(Name="X-Student-Id")] string studentId, CancellationToken ct)
    {
        if (!AdmissionService.ValidStudentId(studentId)) return BadRequest(new ApiError("Invalid X-Student-Id."));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var snapshot = await db.Holds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == holdId, ct);
        if (snapshot is null) { await tx.CommitAsync(ct); return NoContent(); }
        // Use the same per-class lock as allocation/confirmation to avoid a cancel-confirm race.
        var lockedClass = await db.Classes.FromSqlInterpolated(
            $"SELECT * FROM admission_classes WHERE \"Id\" = {snapshot.ClassId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
        if (lockedClass is null) return NotFound(new ApiError("Class not found."));
        var h = await db.Holds.FirstOrDefaultAsync(x => x.Id == holdId, ct);
        if (h is null) { await tx.CommitAsync(ct); return NoContent(); }
        if (h.StudentId != studentId) return Forbid();
        if (h.Status == "HELD") { h.Status = "CANCELLED"; await db.SaveChangesAsync(ct); }
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpGet("/me/holds")]
    public async Task<IActionResult> Mine([FromHeader(Name="X-Student-Id")] string studentId, CancellationToken ct)
    {
        if (!AdmissionService.ValidStudentId(studentId)) return BadRequest(new ApiError("Invalid X-Student-Id."));
        var now = DateTimeOffset.UtcNow;
        var expired = await db.Holds.Where(h => h.StudentId == studentId && h.Status == "HELD" && h.ExpiresAt <= now).ToListAsync(ct);
        foreach (var h in expired) h.Status = "EXPIRED";
        if (expired.Count > 0) await db.SaveChangesAsync(ct);
        var rows = await db.Holds.AsNoTracking().Where(h => h.StudentId == studentId)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new HoldListDto(h.Id, h.ClassId, h.Class!.Name, h.Status, h.ExpiresAt))
            .ToListAsync(ct);
        return Ok(rows);
    }
}
