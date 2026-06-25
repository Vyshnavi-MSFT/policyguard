using Microsoft.EntityFrameworkCore;
using PolicyGuard.Agent;
using PolicyGuard.Data;
using PolicyGuard.Detection;
using PolicyGuard.Workers;

// Load backend/.env (if present) so Azure OpenAI keys are available to PolicyStore/LlmReasoner.
DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

// ===== Database (EF Core + SQLite) =====
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? builder.Configuration["SQLITE_CONNECTION_STRING"]
    ?? "Data Source=policyguard.db";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// ===== Dependency Injection =====
// Controllers are auto-registered by AddControllers

// Detection scanners (Person C). Stateless, so registered as singletons.
builder.Services.AddSingleton<RegexScanner>();
builder.Services.AddSingleton<RoslynScanner>();
builder.Services.AddSingleton<CodeScanner>();

// Person F: policy store (embeddings + retrieval), LLM reasoner, and the reasoning orchestrator.
// Singletons so the policy corpus is loaded/embedded only once for the process lifetime.
builder.Services.AddSingleton<PolicyStore>();
builder.Services.AddSingleton<LlmReasoner>();
builder.Services.AddSingleton<ScanOrchestrator>();

// Dataset detection (Person D): Azure AI Language PII client + dataset scanner.
// AzurePiiOptions.FromEnvironment() reads AZURE_LANGUAGE_* (loaded by DotEnv above);
// the client falls back to offline fake-mode when no endpoint/key is configured.
builder.Services.AddSingleton(AzurePiiOptions.FromEnvironment());
builder.Services.AddSingleton(sp => new AzurePiiClient(new HttpClient(), sp.GetRequiredService<AzurePiiOptions>()));
builder.Services.AddSingleton<DatasetScanner>();

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
    // EnsureCreated builds the schema directly from the model. Used instead of Migrate()
    // because the InitialCreate migration is missing its [Migration] attribute/Designer,
    // so EF doesn't recognize it and Migrate() would create an empty (table-less) database.
    db.Database.EnsureCreated();
}

app.Run();
