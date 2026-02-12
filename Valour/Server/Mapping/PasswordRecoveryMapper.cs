namespace Valour.Server.Mapping;

public static class PasswordRecoveryMapper
{
    public static PasswordRecovery ToModel(this Valour.Database.PasswordRecovery state)
    {
        if (state is null)
            return null;
        
        return new PasswordRecovery()
        {
            Code = state.Code,
            UserId = state.UserId,
            CreatedAt = state.CreatedAt,
            ExpiresAt = state.ExpiresAt
        };
    }
    
    public static Valour.Database.PasswordRecovery ToDatabase(this PasswordRecovery state)
    {
        if (state is null)
            return null;
        
        return new Valour.Database.PasswordRecovery()
        {
            Code = state.Code,
            UserId = state.UserId,
            CreatedAt = state.CreatedAt,
            ExpiresAt = state.ExpiresAt
        };
    }
}
