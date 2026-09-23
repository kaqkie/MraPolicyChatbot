using MraPolicyChatbot.Data;
using MraPolicyChatbot.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var mvcBuilder = builder.Services.AddControllersWithViews();

// Diagnostic fix: without this, .cshtml edits never show up until a full
// rebuild (see the matching PackageReference note in the .csproj) because
// Razor views are compiled into the assembly once at build time by
// default. Dev-only — production still uses the precompiled views, which
// is the correct/faster behavior for a real deployment.
if (builder.Environment.IsDevelopment())
{
    mvcBuilder.AddRazorRuntimeCompilation();
}

// Phase 2: data access layer registrations. Plain DI wiring only — no
// authentication, no session handling, no business logic here.
builder.Services.AddSingleton<DbConnectionFactory>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<PolicyRepository>();
builder.Services.AddScoped<PolicyChunkRepository>();
builder.Services.AddScoped<AuditLogRepository>();

// Backs the Admin > Settings page (AI connection details, the similarity
// threshold, the department list) — see Data/AppSettingsRepository.cs.
builder.Services.AddScoped<AppSettingsRepository>();

// Phase 5: document extraction/chunking pipeline. Scoped like the
// repositories above — DocumentProcessingService is resolved inside its
// own DI scope by AdminController's background trigger, not the request's.
builder.Services.AddScoped<PdfTextExtractor>();
builder.Services.AddScoped<DocxTextExtractor>();
builder.Services.AddScoped<DocumentProcessingService>();

// Phase 6: local AI chat via Ollama (no cloud AI API). AddHttpClient<T>
// gives OllamaClient a pooled HttpClient; ChatService is scoped since it
// depends on the scoped PolicyChunkRepository.
builder.Services.AddHttpClient<OllamaClient>();
builder.Services.AddScoped<ChatService>();

// Phase 3: session-based login. 30-minute idle timeout; ASP.NET Core's
// session middleware is sliding by nature — the timeout resets on every
// request that touches the session, so active users are never logged out
// mid-use.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.IsEssential = true;
});

// Phase 6: the chat page's Send button POSTs a JSON body (not a form), so
// the antiforgery token travels as a header instead of a hidden field.
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
