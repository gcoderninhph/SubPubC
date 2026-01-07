using SubPubC;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddPubSubC();
var app = builder.Build();

app.Run();
