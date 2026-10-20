var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddValidation();  

//builder.Services.AddDbContext<AppDbCtx>(opt => opt.UseSqlServer(configuration.GetConnectionString("dbconn")));

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapGet("/health", () => Results.Ok(new { status = "OK, It's working" }));

app.Run();
