using Valour.Database.Items;

namespace Valour.Database;

public interface ICacheable<T> where T : Item
{
    /// <summary>
    /// Finds and returns the given object id
    /// </summary>
    public async Task<T> FindAsync(object id, ValourDB db)
    {
        return await db.FindAsync<T>(id);
    }

    /// <summary>
    /// Ensures item changes are saved to the database
    /// </summary>
    public async void SaveToDB(ValourDB db)
    {
        db.Update(this);
        await db.SaveChangesAsync();
    }
}

public class DBCache
{

}

