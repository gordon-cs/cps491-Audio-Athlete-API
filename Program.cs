using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);


var corsOrigin = builder.Configuration["CORS_ORIGIN"] ?? "*";

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy
            .WithOrigins(corsOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod());
});


builder.Services.AddControllers();

var app = builder.Build();


app.UseCors("AllowFrontend");

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.Run();
