var builder = WebApplication.CreateBuilder(args);

#region DataProtection

builder.Services.AddDataProtection().UseCryptographicAlgorithms(
        new AuthenticatedEncryptorConfiguration {
            EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
            ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
        })
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["KeyPath"] ??
                                               throw new Exception("KeyPath not found")));

#endregion

builder.Services.AddDbContextFactory<CloudCordDbContext>(
    options => {
        options.UseMySql(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            new MySqlServerVersion(
                ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))),
            optionsBuilder => { optionsBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery); }
        );
        options.EnableSensitiveDataLogging();
        options.UseLoggerFactory(new NullLoggerFactory());
    },
    ServiceLifetime.Transient
);

builder.Services.AddLogging();
builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions();

builder.Services.AddScoped<Repository<FileEntry>>();


builder.Services.AddHttpClient("default");

builder.Services.AddCors(o => {
    o.AddPolicy("AllowAll", pb => {
        pb.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


#region Discord

builder.Services.AddSingleton<DcMsgService>();

builder.Services.Configure<DiscordCfg>(f => {
    f.GuildId = ulong.Parse(builder.Configuration["GuildId"] ?? throw new Exception("GuildId not found"));
    f.ChannelId = ulong.Parse(builder.Configuration["ChannelId"] ?? throw new Exception("ChannelId not found"));
});

#endregion


var app = builder.Build();


#region Discord

var mng = app.Services.GetRequiredService<DcMsgService>();

await mng.InitAsync(builder.Configuration.GetRequiredSection("Tokens").GetChildren()
                        .Select(s => s.Value ?? throw new Exception("Token not found"))
                    ?? throw new InvalidOperationException());

#endregion


if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions {
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseCors("AllowAll");

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => Results.File("index.html","text/html"));

app.Run();