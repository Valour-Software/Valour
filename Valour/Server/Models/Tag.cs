namespace Valour.Server.Models;

public class Tag :ServerModel<long>
{
    public long Id { get; set; }
    public string Name { get; set; }
    public DateTime Created { get; set; }
    public string Slug { get; set; }
    
    
    
    
}