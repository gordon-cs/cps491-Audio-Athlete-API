using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------
// ✅ Add CORS configuration here
// -----------------------------------------
var corsOrigin = builder.Configuration["CORS_ORIGIN"] ?? "*";

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy
            .WithOrigins(corsOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

// -----------------------------------------
// Add other services (controllers, etc.)
// -----------------------------------------
builder.Services.AddControllers();

var app = builder.Build();

// -----------------------------------------
// ✅ Enable CORS middleware
// -----------------------------------------
app.UseCors("AllowFrontend");

// If using HTTPS redirection and authorization, keep them
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.Run();
