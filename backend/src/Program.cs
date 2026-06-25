using Microsoft.EntityFrameworkCore;
using PolicyGuard.Data;
using PolicyGuard.Workers;

var builder = WebApplicationBuilder.CreateBuilder(args);

// ===== Database (EF Core + SQLite) =====
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? builder.Configuration["SQLITE_CONNECTION_STRING"]
    ?? "Data Source=policyguard.db";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// ===== Dependency Injection =====
// Controllers are auto-registered by AddControllers

// ===== Background Services =====
builder.Services.AddHostedService<ScanBackgroundService>();

// ===== CORS =====
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")  // Vite dev / typical frontend ports
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// ===== Swagger/OpenAPI =====
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ===== Controllers =====
builder.Services.AddControllers();

// ===== Build =====
var app = builder.Build();

// ===== Middleware =====
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.MapControllers();

// ===== Database Migration =====
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
