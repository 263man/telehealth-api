using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace TelehealthApi.Core
{
    public class TelehealthDbContextFactory : IDesignTimeDbContextFactory<TelehealthDbContext>
    {
        public TelehealthDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "TelehealthApi.Api"))
                .AddJsonFile("appsettings.json")
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            var optionsBuilder = new DbContextOptionsBuilder<TelehealthDbContext>();
            optionsBuilder.UseSqlServer(connectionString);

            return new TelehealthDbContext(optionsBuilder.Options);
        }
    }
}
