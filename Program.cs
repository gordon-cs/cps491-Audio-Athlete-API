var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

var app = builder.Build();

app.MapGet("/test", () => "Hello World!"); // Optional test endpoint

app.MapControllers();

app.UseAuthorization();

app.Run();

