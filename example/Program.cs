using Atlas.Example.Data;
using Atlas.Example.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Example;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDbContext<ExampleDbContext>(options =>
            options
                .UseSqlite("Data Source=atlas.db")
                .EnableSensitiveDataLogging());

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExampleDbContext>();
            db.Database.EnsureCreated();
            DataSeeder.Seed(db);
        }

        app.MapAtlasEndpoints();
        app.MapUsersEndpoints();

        app.MapGet("/", () => Results.Ok(new
        {
            message = "Atlas Example API",
            endpoints = new
            {
                query = "POST /api/query",
                entities = "GET /api/entities",
                queryUsers = "POST /api/users/query (legacy)",
                listUsers = "GET /api/users"
            }
        }));

        app.Run();
    }
}
