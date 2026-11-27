using System.Text.Json;
using System.Text.Json.Serialization;
using EEMP.Api;
using EEMP.Api.Seeding;
using EEMP.Infrastructure;
using EEMP.Infrastructure.Persistence;
using EEMP.Modules.Academic.Infrastructure;
using EEMP.Modules.Identity.Infrastructure;
using EEMP.Modules.Organization.Infrastructure;
using EEMP.Modules.People.Infrastructure;
using EEMP.Modules.Tenancy.Infrastructure;
using EEMP.Modules.Workflow.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Json;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs. Request bodies, tokens and personal data are never logged.
builder.Services.AddSerilog((services, cfg) => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

builder.Services.AddEempInfrastructure(builder.Configuration);
builder.Services.AddTenancyModule();
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddOrganizationModule();
builder.Services.AddPeopleModule();
builder.Services.AddAcademicModule();
builder.Services.AddWorkflowModule(builder.Configuration);
builder.Services.AddDevelopmentSeed(builder.Configuration);

builder.Services.AddOpenApi("v1");
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var otel = builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("eemp-api"))
    .WithTracing(t => t.AddAspNetCoreInstrumentation(o => o.RecordException = true));
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    builder.Services.ConfigureOpenTelemetryTracerProvider(t => t.AddOtlpExporter());

const string DevCors = "dev-frontends";
builder.Services.AddCors(o => o.AddPolicy(DevCors, p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod()
    .WithExposedHeaders("X-Correlation-Id", "Retry-After")));

var app = builder.Build();

app.UseEempRequestPipeline(a => a.UseSerilogRequestLogging(o => o.GetLevel = (http, _, ex) =>
    ex is not null || http.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error : Serilog.Events.LogEventLevel.Information));
if (app.Environment.IsDevelopment()) app.UseCors(DevCors);
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapOpenApi("/openapi/{documentName}.json");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

var api = app.MapGroup("/api/v1");
api.MapTenancyEndpoints();
api.MapIdentityEndpoints();
api.MapOrganizationEndpoints();
api.MapPeopleEndpoints();
api.MapAcademicEndpoints();
api.MapWorkflowEndpoints();

await app.Services.InitializeDatabasesAsync();
await app.Services.RunDevelopmentSeedAsync();

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory integration tests.</summary>
public partial class Program;
