using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);
var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=db;Port=5432;Database=admission;Username=admission;Password=admission";
var holdSeconds = int.TryParse(Environment.GetEnvironmentVariable("HOLD_DURATION_SECONDS"), out var configuredHold) && configuredHold > 0
    ? configuredHold : 600;
var adminRegistrationKey = Environment.GetEnvironmentVariable("ADMIN_REGISTRATION_KEY") ?? "local-school-admin-key";
var secureAdminCookie = string.Equals(Environment.GetEnvironmentVariable("ADMIN_COOKIE_SECURE"), "true", StringComparison.OrdinalIgnoreCase);
const string AdminCookieName = "school_admin";
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddHostedService<ExpiryWorker>();
var allowedOrigins = (Environment.GetEnvironmentVariable("FRONTEND_ORIGIN") ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();
app.UseCors();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (BadHttpRequestException)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "The request body or parameters are invalid." });
        }
    }
    catch (NpgsqlException)
    {
        app.Logger.LogError("Database request failed for {Path}", context.Request.Path);
        if (!context.Response.HasStarted) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { message = "Service temporarily unavailable. Please retry." }); }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Unhandled request error for {Path}", context.Request.Path);
        if (!context.Response.HasStarted) { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { message = "The request could not be completed." }); }
    }
});

var db = app.Services.GetRequiredService<NpgsqlDataSource>();
await Init(db);
app.Use(async (context, next) =>
{
    var token = context.Request.Cookies[AdminCookieName];
    if (!string.IsNullOrEmpty(token))
    {
        await using var session = db.CreateCommand("SELECT a.id,a.username FROM admin_sessions s JOIN admins a ON a.id=s.admin_id WHERE s.token_hash=@hash AND s.expires_at>now()");
        session.Parameters.AddWithValue("hash", HashSessionToken(token));
        await using var reader = await session.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            context.Items["adminId"] = reader.GetString(0);
            context.Items["adminUsername"] = reader.GetString(1);
        }
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/ready", async () =>
{
    await using var cmd = db.CreateCommand("SELECT 1");
    await cmd.ExecuteScalarAsync();
    return Results.Ok(new { status = "ready" });
});

app.MapPost("/admin/register", async (AdminRegister input, HttpContext context) =>
{
    var username = input.UserName?.Trim() ?? "";
    var password = input.Password ?? "";
    if (!Regex.IsMatch(username, @"^[A-Za-z0-9._-]{3,40}$", RegexOptions.CultureInvariant) || password.Length is < 12 or > 128)
        return (IResult)Results.BadRequest(new { message = "Use a 3–40 character username and a password of 12–128 characters." });
    if (!ConstantTimeEquals(input.RegistrationKey ?? "", adminRegistrationKey))
        return (IResult)Results.Json(new { message = "The school administrator registration key is not valid." }, statusCode: 403);

    var salt = RandomNumberGenerator.GetBytes(16);
    var passwordHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, 32);
    await using var create = db.CreateCommand("INSERT INTO admins(id,username,password_salt,password_hash) VALUES(@id,@username,@salt,@hash) ON CONFLICT DO NOTHING RETURNING id");
    create.Parameters.AddWithValue("id", "a_" + Guid.NewGuid().ToString("N"));
    create.Parameters.AddWithValue("username", username);
    create.Parameters.AddWithValue("salt", salt);
    create.Parameters.AddWithValue("hash", passwordHash);
    var adminId = (string?)await create.ExecuteScalarAsync();
    if (adminId is null) return (IResult)Results.Conflict(new { message = "That administrator username is already registered." });
    var token = await CreateAdminSession(db, adminId);
    SetAdminCookie(context, token, secureAdminCookie);
    return (IResult)Results.Created("/admin/me", new { userName = username });
});

app.MapPost("/admin/login", async (AdminLogin input, HttpContext context) =>
{
    var username = input.UserName?.Trim() ?? "";
    await using var lookup = db.CreateCommand("SELECT id,username,password_salt,password_hash FROM admins WHERE lower(username)=lower(@username)");
    lookup.Parameters.AddWithValue("username", username);
    await using var reader = await lookup.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return (IResult)Results.Json(new { message = "Username or password is incorrect." }, statusCode: 401);
    var adminId = reader.GetString(0);
    var canonicalUsername = reader.GetString(1);
    var salt = (byte[])reader[2];
    var expectedHash = (byte[])reader[3];
    await reader.CloseAsync();
    var password = input.Password ?? "";
    var suppliedHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 310_000, HashAlgorithmName.SHA256, expectedHash.Length);
    if (!CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash))
        return (IResult)Results.Json(new { message = "Username or password is incorrect." }, statusCode: 401);
    var token = await CreateAdminSession(db, adminId);
    SetAdminCookie(context, token, secureAdminCookie);
    return (IResult)Results.Ok(new { userName = canonicalUsername });
});

app.MapGet("/admin/me", (HttpContext context) =>
{
    if (!IsAdmin(context)) return (IResult)Results.Json(new { message = "Please sign in as a school administrator." }, statusCode: 401);
    return (IResult)Results.Ok(new { userName = context.Items["adminUsername"] });
});

app.MapPost("/admin/logout", async (HttpContext context) =>
{
    var token = context.Request.Cookies[AdminCookieName];
    if (!string.IsNullOrEmpty(token))
    {
        await using var revoke = db.CreateCommand("DELETE FROM admin_sessions WHERE token_hash=@hash");
        revoke.Parameters.AddWithValue("hash", HashSessionToken(token));
        await revoke.ExecuteNonQueryAsync();
    }
    context.Response.Cookies.Delete(AdminCookieName, new CookieOptions { Path = "/", SameSite = SameSiteMode.Lax, Secure = secureAdminCookie, HttpOnly = true });
    return Results.Ok(new { message = "Signed out." });
});

app.MapPost("/classes", async (CreateClass input, HttpContext context) =>
{
    if (!IsAdmin(context)) return (IResult)Results.Json(new { message = "Sign in to the school admin panel to create a class." }, statusCode: 401);
    if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 120 || input.TotalSeats is < 1 or > 100000 || input.AdmissionFeeMinorUnits < 0)
        return (IResult)Results.BadRequest(new { message = "Enter a valid class, seat count, and non-negative admission fee." });
    await using var cmd = db.CreateCommand("INSERT INTO classes(id,name,total_seats,admission_fee_minor_units) VALUES(@id,@name,@seats,@fee) RETURNING id,name,total_seats,admission_fee_minor_units");
    cmd.Parameters.AddWithValue("id", "c_" + Guid.NewGuid().ToString("N"));
    cmd.Parameters.AddWithValue("name", input.Name.Trim()); cmd.Parameters.AddWithValue("seats", input.TotalSeats); cmd.Parameters.AddWithValue("fee", input.AdmissionFeeMinorUnits);
    await using var reader = await cmd.ExecuteReaderAsync(); await reader.ReadAsync();
    return (IResult)Results.Created($"/classes/{reader.GetString(0)}", new { id = reader.GetString(0), name = reader.GetString(1), totalSeats = reader.GetInt32(2), admissionFeeMinorUnits = reader.GetInt32(3) });
});

app.MapPut("/classes/{id}/capacity", async (string id, UpdateCapacity input, HttpContext context) =>
{
    if (!IsAdmin(context)) return (IResult)Results.Json(new { message = "Sign in to the school admin panel to change seat capacity." }, statusCode: 401);
    if (input.TotalSeats is < 1 or > 100000) return (IResult)Results.BadRequest(new { message = "Seat capacity must be between 1 and 100,000." });
    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var classLock = new NpgsqlCommand("SELECT total_seats FROM classes WHERE id=@id FOR UPDATE", connection, transaction);
    classLock.Parameters.AddWithValue("id", id);
    if (await classLock.ExecuteScalarAsync() is null) return (IResult)Results.NotFound(new { message = "Class not found." });
    await using (var expire = new NpgsqlCommand("UPDATE holds SET status='EXPIRED' WHERE class_id=@class AND status='HELD' AND expires_at<=now()", connection, transaction))
    { expire.Parameters.AddWithValue("class", id); await expire.ExecuteNonQueryAsync(); }
    await using (var occupied = new NpgsqlCommand("SELECT count(*) FROM holds WHERE class_id=@class AND (status='CONFIRMED' OR(status='HELD' AND expires_at>now()))", connection, transaction))
    {
        occupied.Parameters.AddWithValue("class", id);
        if ((long)(await occupied.ExecuteScalarAsync())! > input.TotalSeats)
            return (IResult)Results.Conflict(new { message = "Capacity cannot be lower than the number of held and confirmed seats." });
    }
    await using (var update = new NpgsqlCommand("UPDATE classes SET total_seats=@seats WHERE id=@id", connection, transaction))
    { update.Parameters.AddWithValue("seats", input.TotalSeats); update.Parameters.AddWithValue("id", id); await update.ExecuteNonQueryAsync(); }
    await transaction.CommitAsync();
    return (IResult)Results.Ok((await Classes(db, id))[0]);
});

app.MapGet("/admin/classes/{id}/students", async (string id, HttpContext context) =>
{
    if (!IsAdmin(context)) return (IResult)Results.Json(new { message = "Sign in to the school admin panel to view admitted students." }, statusCode: 401);
    await using var cmd = db.CreateCommand("SELECT h.id,h.student_id,h.student_name,h.payment_verified_at FROM holds h WHERE h.class_id=@class AND h.status='CONFIRMED' ORDER BY h.payment_verified_at,h.created_at");
    cmd.Parameters.AddWithValue("class", id);
    await using var reader = await cmd.ExecuteReaderAsync();
    var students = new List<object>();
    while (await reader.ReadAsync()) students.Add(new { holdId = reader.GetString(0), studentId = reader.GetString(1).Trim(), studentName = reader.GetString(2), confirmedAt = reader.IsDBNull(3) ? (DateTime?)null : DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc) });
    return (IResult)Results.Ok(students);
});
app.MapGet("/classes", async () => Results.Ok(await Classes(db, null)));
app.MapGet("/classes/{id}", async (string id) =>
{
    var rows = await Classes(db, id);
    if (rows.Count == 0) return (IResult)Results.NotFound(new { message = "Class not found." });
    return (IResult)Results.Ok(rows[0]);
});

app.MapPost("/classes/{id}/holds", async (string id, HttpRequest request) =>
{
    var studentId = request.Headers["X-Student-Id"].ToString();
    var requestedName = request.Headers["X-Student-Name"].ToString();
    var studentName = string.IsNullOrWhiteSpace(requestedName) ? "Student" : requestedName.Trim();
    if (!ValidStudent(studentId) || studentName.Length > 160)
        return (IResult)Results.BadRequest(new { message = "Enter a valid 17-digit registration number and student name." });
    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await using var classLock = new NpgsqlCommand("SELECT total_seats FROM classes WHERE id=@id FOR UPDATE", connection, transaction);
    classLock.Parameters.AddWithValue("id", id);
    var seatCount = await classLock.ExecuteScalarAsync();
    if (seatCount is null) return (IResult)Results.NotFound(new { message = "Class not found." });
    await using (var expire = new NpgsqlCommand("UPDATE holds SET status='EXPIRED' WHERE class_id=@class AND status='HELD' AND expires_at<=now()", connection, transaction))
    { expire.Parameters.AddWithValue("class", id); await expire.ExecuteNonQueryAsync(); }
    await using (var duplicate = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM holds WHERE class_id=@class AND student_id=@student AND status IN('HELD','CONFIRMED'))", connection, transaction))
    {
        duplicate.Parameters.AddWithValue("class", id); duplicate.Parameters.AddWithValue("student", studentId);
        if ((bool)(await duplicate.ExecuteScalarAsync())!) return (IResult)Results.Conflict(new { message = "You already have an active application for this class." });
    }
    await using (var capacity = new NpgsqlCommand("SELECT count(*) FROM holds WHERE class_id=@class AND (status='CONFIRMED' OR(status='HELD' AND expires_at>now()))", connection, transaction))
    {
        capacity.Parameters.AddWithValue("class", id);
        if ((long)(await capacity.ExecuteScalarAsync())! >= (int)seatCount) return (IResult)Results.Conflict(new { message = "No seats are currently available." });
    }
    var holdId = "h_" + Guid.NewGuid().ToString("N");
    await using var insert = new NpgsqlCommand("INSERT INTO holds(id,class_id,student_id,student_name,status,expires_at,admission_fee_minor_units) VALUES(@id,@class,@student,@name,'HELD',now()+(@duration*interval '1 second'),(SELECT admission_fee_minor_units FROM classes WHERE id=@class)) RETURNING expires_at,admission_fee_minor_units", connection, transaction);
    insert.Parameters.AddWithValue("id", holdId); insert.Parameters.AddWithValue("class", id); insert.Parameters.AddWithValue("student", studentId);
    insert.Parameters.AddWithValue("name", studentName); insert.Parameters.AddWithValue("duration", holdSeconds);
    await using var row = await insert.ExecuteReaderAsync(); await row.ReadAsync(); var expiresAt = row.GetDateTime(0); var fee = row.GetInt32(1); await row.CloseAsync();
    await transaction.CommitAsync();
    return (IResult)Results.Created($"/holds/{holdId}", new { holdId, classId = id, studentId, studentName, status = "HELD", admissionFeeMinorUnits = fee, expiresAt = DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc) });
});

app.MapGet("/holds/{id}", async (string id) =>
{
    await using (var expire = db.CreateCommand("UPDATE holds SET status='EXPIRED' WHERE id=@id AND status='HELD' AND expires_at<=now()"))
    { expire.Parameters.AddWithValue("id", id); await expire.ExecuteNonQueryAsync(); }
    await using var cmd = db.CreateCommand("SELECT id,class_id,status,expires_at FROM holds WHERE id=@id");
    cmd.Parameters.AddWithValue("id", id);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return (IResult)Results.NotFound(new { message = "Application not found." });
    return (IResult)Results.Ok(new { holdId = reader.GetString(0), classId = reader.GetString(1), status = reader.GetString(2), expiresAt = DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc) });
});

app.MapPost("/holds/{id}/confirm", async (string id, HttpRequest request) =>
{
    var student = request.Headers["X-Student-Id"].ToString();
    if (!ValidStudent(student)) return (IResult)Results.BadRequest(new { message = "Enter a valid 17-digit registration number." });
    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var classId = await LookupClass(connection, transaction, id);
    if (classId is null) return (IResult)Results.NotFound(new { message = "Application not found." });
    await LockClass(connection, transaction, classId);
    await using var query = new NpgsqlCommand("SELECT student_id,status,(expires_at>now()) FROM holds WHERE id=@id FOR UPDATE", connection, transaction);
    query.Parameters.AddWithValue("id", id);
    await using var reader = await query.ExecuteReaderAsync();
    if (!await reader.ReadAsync()) return (IResult)Results.NotFound(new { message = "Application not found." });
    var owner = reader.GetString(0).Trim(); var status = reader.GetString(1); var isLive = reader.GetBoolean(2); await reader.CloseAsync();
    if (owner != student) return (IResult)Results.Json(new { message = "This application belongs to another student." }, statusCode: 403);
    if (status == "CONFIRMED") return (IResult)Results.Ok(new { holdId = id, status });
    if (status != "HELD" || !isLive) return (IResult)Results.Conflict(new { message = "This hold has expired or is no longer active." });
    await using var confirm = new NpgsqlCommand("UPDATE holds SET status='CONFIRMED',payment_status='PAID',payment_verified_at=now() WHERE id=@id", connection, transaction);
    confirm.Parameters.AddWithValue("id", id); await confirm.ExecuteNonQueryAsync();
    await transaction.CommitAsync();
    return (IResult)Results.Ok(new { holdId = id, status = "CONFIRMED" });
});

app.MapDelete("/holds/{id}", async (string id, HttpRequest request) =>
{
    var student = request.Headers["X-Student-Id"].ToString();
    if (!ValidStudent(student)) return (IResult)Results.BadRequest(new { message = "Invalid registration number." });
    await using var connection = await db.OpenConnectionAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var classId = await LookupClass(connection, transaction, id);
    if (classId is null) return (IResult)Results.NotFound(new { message = "Application not found." });
    await LockClass(connection, transaction, classId);
    await using var cancel = new NpgsqlCommand("UPDATE holds SET status='CANCELLED' WHERE id=@id AND student_id=@student AND status='HELD' AND expires_at>now() RETURNING id", connection, transaction);
    cancel.Parameters.AddWithValue("id", id); cancel.Parameters.AddWithValue("student", student);
    if (await cancel.ExecuteScalarAsync() is null) return (IResult)Results.Conflict(new { message = "This application cannot be cancelled." });
    await transaction.CommitAsync(); return (IResult)Results.NoContent();
});

app.MapGet("/me/holds", async (HttpRequest request) =>
{
    var student = request.Headers["X-Student-Id"].ToString();
    if (!ValidStudent(student)) return (IResult)Results.BadRequest(new { message = "Invalid registration number." });
    await using var cmd = db.CreateCommand("SELECT h.id,h.class_id,c.name,h.student_name,CASE WHEN h.status='HELD' AND h.expires_at<=now() THEN 'EXPIRED' ELSE h.status END,h.expires_at,h.admission_fee_minor_units,h.payment_status FROM holds h JOIN classes c ON c.id=h.class_id WHERE h.student_id=@student ORDER BY h.created_at DESC");
    cmd.Parameters.AddWithValue("student", student); await using var reader = await cmd.ExecuteReaderAsync(); var items = new List<object>();
    while (await reader.ReadAsync()) items.Add(new { holdId = reader.GetString(0), classId = reader.GetString(1), className = reader.GetString(2), studentName = reader.GetString(3), status = reader.GetString(4), expiresAt = DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc), admissionFeeMinorUnits = reader.GetInt32(6), paymentStatus = reader.GetString(7) });
    return (IResult)Results.Ok(items);
});

app.Run();

static bool ValidStudent(string student) => Regex.IsMatch(student ?? "", @"^[0-9]{17}$", RegexOptions.CultureInvariant);
static bool IsAdmin(HttpContext context) => context.Items.ContainsKey("adminId");
static bool ConstantTimeEquals(string provided, string expected)
{
    var providedBytes = Encoding.UTF8.GetBytes(provided);
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    return providedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
}
static string HashSessionToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
static async Task<string> CreateAdminSession(NpgsqlDataSource db, string adminId)
{
    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    await using (var cleanup = db.CreateCommand("DELETE FROM admin_sessions WHERE expires_at<=now()")) await cleanup.ExecuteNonQueryAsync();
    await using var insert = db.CreateCommand("INSERT INTO admin_sessions(token_hash,admin_id,expires_at) VALUES(@hash,@admin,now()+interval '12 hours')");
    insert.Parameters.AddWithValue("hash", HashSessionToken(token));
    insert.Parameters.AddWithValue("admin", adminId);
    await insert.ExecuteNonQueryAsync();
    return token;
}
static void SetAdminCookie(HttpContext context, string token, bool secure) => context.Response.Cookies.Append("school_admin", token, new CookieOptions
{
    HttpOnly = true,
    Secure = secure,
    SameSite = SameSiteMode.Lax,
    Path = "/",
    IsEssential = true,
    MaxAge = TimeSpan.FromHours(12)
});
static async Task<string?> LookupClass(NpgsqlConnection connection, NpgsqlTransaction transaction, string id)
{
    await using var cmd = new NpgsqlCommand("SELECT class_id FROM holds WHERE id=@id", connection, transaction); cmd.Parameters.AddWithValue("id", id); return (string?)await cmd.ExecuteScalarAsync();
}
static async Task LockClass(NpgsqlConnection connection, NpgsqlTransaction transaction, string classId)
{
    await using var cmd = new NpgsqlCommand("SELECT id FROM classes WHERE id=@id FOR UPDATE", connection, transaction); cmd.Parameters.AddWithValue("id", classId); await cmd.ExecuteScalarAsync();
}
static async Task<List<object>> Classes(NpgsqlDataSource db, string? id)
{
    var sql = "SELECT c.id,c.name,c.total_seats,c.admission_fee_minor_units,count(*) FILTER(WHERE h.status='HELD' AND h.expires_at>now())::int,count(*) FILTER(WHERE h.status='CONFIRMED')::int FROM classes c LEFT JOIN holds h ON h.class_id=c.id" + (id is null ? "" : " WHERE c.id=@id") + " GROUP BY c.id ORDER BY c.created_at";
    await using var cmd = db.CreateCommand(sql); if (id is not null) cmd.Parameters.AddWithValue("id", id); await using var reader = await cmd.ExecuteReaderAsync(); var result = new List<object>();
    while (await reader.ReadAsync()) result.Add(new { id = reader.GetString(0), name = reader.GetString(1), totalSeats = reader.GetInt32(2), admissionFeeMinorUnits = reader.GetInt32(3), heldSeats = reader.GetInt32(4), confirmedSeats = reader.GetInt32(5), availableSeats = reader.GetInt32(2) - reader.GetInt32(4) - reader.GetInt32(5) });
    return result;
}
static async Task Init(NpgsqlDataSource db)
{
    const string sql = "CREATE TABLE IF NOT EXISTS classes(id text PRIMARY KEY,name varchar(120) NOT NULL,total_seats integer NOT NULL CHECK(total_seats>0),created_at timestamptz NOT NULL DEFAULT now());" +
        "ALTER TABLE classes ADD COLUMN IF NOT EXISTS admission_fee_minor_units integer NOT NULL DEFAULT 0 CHECK(admission_fee_minor_units>=0);" +
        "CREATE TABLE IF NOT EXISTS holds(id text PRIMARY KEY,class_id text NOT NULL REFERENCES classes(id),student_id char(17) NOT NULL CHECK(student_id ~ '^[0-9]{17}$'),status text NOT NULL CHECK(status IN('HELD','CONFIRMED','EXPIRED','CANCELLED')),expires_at timestamptz NOT NULL,created_at timestamptz NOT NULL DEFAULT now());" +
        "CREATE TABLE IF NOT EXISTS admins(id text PRIMARY KEY,username varchar(40) NOT NULL,password_salt bytea NOT NULL,password_hash bytea NOT NULL,created_at timestamptz NOT NULL DEFAULT now());" +
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_admins_username_ci ON admins(lower(username));" +
        "CREATE TABLE IF NOT EXISTS admin_sessions(token_hash char(64) PRIMARY KEY,admin_id text NOT NULL REFERENCES admins(id) ON DELETE CASCADE,expires_at timestamptz NOT NULL,created_at timestamptz NOT NULL DEFAULT now());" +
        "ALTER TABLE holds ADD COLUMN IF NOT EXISTS student_name varchar(160) NOT NULL DEFAULT 'Student';" +
        "ALTER TABLE holds ADD COLUMN IF NOT EXISTS admission_fee_minor_units integer NOT NULL DEFAULT 0 CHECK(admission_fee_minor_units>=0);" +
        "ALTER TABLE holds ADD COLUMN IF NOT EXISTS payment_status text NOT NULL DEFAULT 'UNPAID';" +
        "ALTER TABLE holds ADD COLUMN IF NOT EXISTS payment_reference varchar(120);" +
        "ALTER TABLE holds ADD COLUMN IF NOT EXISTS payment_submitted_at timestamptz;" +
        "ALTER TABLE holds ADD COLUMN IF NOT EXISTS payment_verified_at timestamptz;" +
        "CREATE INDEX IF NOT EXISTS ix_holds_class_state ON holds(class_id,status,expires_at);CREATE INDEX IF NOT EXISTS ix_holds_student_class ON holds(student_id,class_id,status);" +
        "CREATE UNIQUE INDEX IF NOT EXISTS ux_holds_payment_reference ON holds(payment_reference) WHERE payment_reference IS NOT NULL;";
    await using var cmd = db.CreateCommand(sql); await cmd.ExecuteNonQueryAsync();
}

record CreateClass(string Name, int TotalSeats, int AdmissionFeeMinorUnits);
record AdminRegister(string UserName, string Password, string RegistrationKey);
record AdminLogin(string UserName, string Password);
record UpdateCapacity(int TotalSeats);
public partial class Program { }

sealed class ExpiryWorker(NpgsqlDataSource db) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await using var cmd = db.CreateCommand("UPDATE holds SET status='EXPIRED' WHERE status='HELD' AND expires_at<=now()"); await cmd.ExecuteNonQueryAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (NpgsqlException ex) { Console.Error.WriteLine($"Expiry worker database error: {ex.SqlState}"); }
        }
    }
}
