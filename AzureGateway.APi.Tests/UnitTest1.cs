using Microsoft.EntityFrameworkCore;
using AzureGateway.Api.Data;

namespace AzureGateway.APi.Tests
{
    public static class TestDbContextFactory
    {
        public static ApplicationDbContext CreateInMemory()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}