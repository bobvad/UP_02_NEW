using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

builder.Services.AddControllers();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "API v1",
        Description = "Первая версия API"
    });

    options.SwaggerDoc("v2", new OpenApiInfo
    {
        Version = "v2",
        Title = "API v2",
        Description = "Вторая версия API"
    });

    options.SwaggerDoc("v3", new OpenApiInfo
    {
        Version = "v3",
        Title = "API v3",
        Description = "Третья версия API"
    });

    options.SwaggerDoc("v4", new OpenApiInfo
    {
        Version = "v4",
        Title = "API v4",
        Description = "Четвертая версия API"
    });

    options.SwaggerDoc("v5", new OpenApiInfo
    {
        Version = "v5",
        Title = "Парсинг данных",
        Description = "Пятая версия API предназначенная для парсинга данных с сайта для книг"
    });

    var xmlFilename = $"API_UP_02.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
        options.SwaggerEndpoint("/swagger/v2/swagger.json", "API v2");
        options.SwaggerEndpoint("/swagger/v3/swagger.json", "API v3");
        options.SwaggerEndpoint("/swagger/v4/swagger.json", "API v4");
        options.SwaggerEndpoint("/swagger/v5/swagger.json", "API for parsing website LitMir Club v5");
    });
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers(); 

app.Run();