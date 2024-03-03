using Microsoft.EntityFrameworkCore;
using trackingapi.Models;

namespace trackingapi.Data
{
    public class IssueDbContext : DbContext
    {
        public DbSet<Entity> Entities { get; set; }

        public IssueDbContext(DbContextOptions<IssueDbContext> options) : base(options)
        {
        }
    }
}