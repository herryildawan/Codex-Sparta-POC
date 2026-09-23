using Microsoft.EntityFrameworkCore;
using Sparta.SharedKernel;

namespace Sparta.Modules.Master;

public class MasterDbContext(DbContextOptions<MasterDbContext> options) : BusinessDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureBusinessModel();
    }
}
