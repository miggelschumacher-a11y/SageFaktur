using System;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WipSageWeb.Data;

namespace WipSageWeb
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<LoginOptions>(_configuration.GetSection("Login"));
            services.AddScoped<ILoginService, LoginService>();
            services.AddScoped<ISageExportService, SageExportService>();

            // Die Anmeldung liegt in der Server-Session; nach 30 Minuten ohne Aktivität läuft sie ab
            services.AddMemoryCache();   // hält vorbereitete Exporte zwischen den Einzelschritten
            services.AddDistributedMemoryCache();
            services.AddSession(options =>
            {
                options.Cookie.Name = ".WipSageWeb.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.IdleTimeout = TimeSpan.FromMinutes(30);
            });

            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);
            services.AddScoped<IWipSageRepository, WipSageRepository>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // Zahlen und Beträge im deutschen Format anzeigen (1.234,56)
            var deDE = new CultureInfo("de-DE");
            app.UseRequestLocalization(new RequestLocalizationOptions
            {
                DefaultRequestCulture = new RequestCulture(deDE),
                SupportedCultures = new[] { deDE },
                SupportedUICultures = new[] { deDE }
            });

            app.UseStaticFiles();
            app.UseSession();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=WipPa}/{action=Index}/{id?}");
            });
        }
    }
}
