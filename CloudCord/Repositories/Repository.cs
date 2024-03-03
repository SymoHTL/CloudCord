namespace CloudCord.Repositories;

public class Repository<TEntity> where TEntity : class {
    protected readonly CloudCordDbContext Context;
    protected readonly DbSet<TEntity> Table;

    public Repository(CloudCordDbContext context) {
        Context = context;
        Table = Context.Set<TEntity>();
    }

    public async Task<List<TEntity>> ReadAsync(Expression<Func<TEntity, bool>> filter, CancellationToken order) {
        return await Table.AsNoTracking()
            .Where(filter)
            .ToListAsync(order);
    }

    public async Task<List<TEntity>> ReadAsync(Expression<Func<TEntity, bool>> filter,
        Expression<Func<TEntity, object>> order, CancellationToken ct) {
        return await Table.AsNoTracking()
            .Where(filter)
            .OrderBy(order)
            .ToListAsync(ct);
    }

    public async Task CreateAsync(IEnumerable<TEntity> entities, CancellationToken ct) {
        await Table.AddRangeAsync(entities, ct);
        await Context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(IEnumerable<TEntity> entities, CancellationToken ct) {
        Table.RemoveRange(entities);
        await Context.SaveChangesAsync(ct);
    }
}