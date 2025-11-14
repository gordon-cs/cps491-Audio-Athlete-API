using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

//--------------------------------------------------//
//                     CORS                          //
//--------------------------------------------------//
var corsOrigin = builder.Configuration["CORS_ORIGIN"] ?? "*";
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
        policy
            .WithOrigins(corsOrigin)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

//--------------------------------------------------//
//                 CONTROLLERS                       //
//--------------------------------------------------//
builder.Services.AddControllers();

//--------------------------------------------------//
//                 JWT AUTHENTICATION                //
//--------------------------------------------------//
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]);
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

var app = builder.Build();

//--------------------------------------------------//
//                  MIDDLEWARE                       //
//--------------------------------------------------//
app.UseCors("AllowFrontend");
app.UseHttpsRedirection();

app.UseAuthentication(); // <-- IMPORTANT: must come before UseAuthorization
app.UseAuthorization();

//--------------------------------------------------//
//                  ENDPOINTS                        //
//--------------------------------------------------//
app.MapControllers();

app.Run();
