using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace KarakusAluminyum.Tests;

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

// Tüm entegrasyon testleri tek bir PostgreSQL konteynerini paylaşır; bu yüzden Docker gerektirir.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private AppFactory? _shared;

    // Veritabanının ilk hâline bağlı olmayan testlerin ortak uygulaması.
    public AppFactory Shared => _shared ??= CreateApp("shared");

    public AppFactory CreateApp(string database, string schemaMode = "ensurecreated")
    {
        var connection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database };
        return new AppFactory(connection.ToString(), schemaMode);
    }

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync()
    {
        if (_shared is not null) await _shared.DisposeAsync();
        await _container.DisposeAsync();
    }
}

public sealed class AppFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public const string Password = "Test-Pass-12345!";

    private readonly string _connection;
    private readonly string _schemaMode;
    private readonly string _tempRoot = Directory.CreateTempSubdirectory("karakus-tests-").FullName;

    public AppFactory(string connection, string schemaMode) => (_connection, _schemaMode) = (connection, schemaMode);

    // Yüklemeler repodaki wwwroot yerine geçici klasöre yazılır.
    public string WebRoot => Path.Combine(_tempRoot, "wwwroot");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(WebRoot);
        builder.UseEnvironment("Development");
        builder.UseWebRoot(WebRoot);
        builder.UseSetting("ConnectionStrings:DefaultConnection", _connection);
        builder.UseSetting("Database:SchemaMode", _schemaMode);
        builder.UseSetting("DataProtection:KeysPath", Path.Combine(_tempRoot, "keys"));
        builder.UseSetting("AdminSeed:Email", AdminEmail);
        builder.UseSetting("AdminSeed:Password", Password);
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task<T> WithServiceAsync<TService, T>(Func<TService, Task<T>> action) where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public Task<T> WithServiceAsync<T>(Func<KarakusAluminyum.Web.Services.Accounting.AccountingQueries, Task<T>> action) => WithServiceAsync<KarakusAluminyum.Web.Services.Accounting.AccountingQueries, T>(action);

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }
}

public static partial class ClientExtensions
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    public static async Task<string> GetTokenAsync(this HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return TokenRegex().Match(html).Groups[1].Value;
    }

    public static async Task LoginAsync(this HttpClient client, string email, string password = AppFactory.Password)
    {
        var token = await client.GetTokenAsync("/yonetim-giris");
        var response = await client.PostFormAsync("/yonetim-giris", token, ("Email", email), ("Password", password));
        Assert.Equal("/yonetim", response.Headers.Location?.OriginalString);
    }

    public static Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string url, string token, params (string Key, string Value)[] fields)
    {
        var values = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).Append(new("__RequestVerificationToken", token));
        return client.PostAsync(url, new FormUrlEncodedContent(values));
    }
}
