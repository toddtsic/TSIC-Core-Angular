using System.Threading;
using Microsoft.Extensions.Hosting;
using TSIC.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Logging: keep console and debug providers for local dev
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "TSIC API", Version = "v1" });
});

// CORS for Angular
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Sample hosted background service that honors cancellation and demonstrates graceful shutdown
builder.Services.AddHostedService<SampleBackgroundService>();

var app = builder.Build();

// Developer UX: show detailed exception page and serve Swagger UI at root in Development
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TSIC API V1");
        c.RoutePrefix = string.Empty; // serve Swagger UI at '/'
    });
}

// Keep logging providers; structured request logging can be added later (Serilog or middleware)

app.UseHttpsRedirection();
app.UseCors("AllowAngularApp");
app.UseAuthorization();
app.MapControllers();

// lightweight health endpoints
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/ready", () => Results.Ok(new { ready = true }));

// Let the host manage graceful shutdown. Use the default app.Run() which respects Ctrl+C/SIGTERM.
await app.RunAsync();
