using System.Text;
using Bob1.Api;
using Bob1.Modules.Carpool;
using Bob1.Modules.Club;
using Bob1.Modules.Identity;
using Bob1.Modules.Matches;
using Bob1.Modules.Notifications;
using Bob1.Modules.Participation;
using Bob1.Modules.Ranking;
using Bob1.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// 1. The modules. Adding a feature area = adding a line here. (Order only matters for dev seeding.)
IModule[] modules =
[
    new ClubModule(),
    new IdentityModule(),
    new MatchesModule(),
    new CarpoolModule(),
    new NotificationsModule(),
    new ParticipationModule(),
    new RankingModule()
];

// 2. Cross-cutting services.
builder.Services.AddShared();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// MVC controllers live in the modules (Features/*Controller.cs); see AddModuleControllers.
builder.Services.AddControllers()
    .AddModuleControllers(modules.Select(m => m.GetType().Assembly));

// Same JSON rules for the non-MVC pipeline (ProblemDetails from the exception handler, OpenAPI).
builder.Services.ConfigureHttpJsonOptions(o => JsonDefaults.Configure(o.SerializerOptions));

// The admin back-office is a separate origin (Next.js dev server on :3000).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? ["http://localhost:3000"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

// 3. Auth: JWT with sub / family_id / role claims (issued by the Identity module).
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
          ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
builder.Services.AddSingleton(jwt);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep "sub" and "role" as-is
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = "sub",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Parent, p => p.RequireRole("Parent"))
    .AddPolicy(Policies.Staff, p => p.RequireRole("Staff"))
    .AddPolicy(Policies.Admin, p => p.RequireRole("Admin"));

foreach (var module in modules)
    module.Register(builder.Services, builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();   // unhandled exceptions -> ProblemDetails 500
app.UseStatusCodePages();    // bare 401/403/404 -> ProblemDetails
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();        // /openapi/v1.json -> generate the Kotlin client from this

// Migrations (+ dev seed data in Development). In production set Database:MigrateOnStartup=true
// for a single instance, or run migrations from your deployment pipeline instead.
var seedRequested = args.Contains("--seed");
if (seedRequested)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:Seed"] = "true" });
}
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Database:MigrateOnStartup") || seedRequested)
{
    using var scope = app.Services.CreateScope();
    foreach (var module in modules)
        await module.InitializeAsync(scope.ServiceProvider);
    if (seedRequested)
        Console.WriteLine("Database seeded successfully.");
}

app.MapControllers();   // routes are declared on the controllers ([Route("api/v1/...")])

app.Run();
