namespace Trion.Core.Setup;

public static class SetupKeys
{
    public const string Completed    = "setup:completed";
    public const string DbType       = "setup:db_type";      // "sqlite" | "mysql"
    public const string DbConnStr    = "setup:db_connstr";
    public const string AccountType    = "setup:account_type"; // "local" | "phoenix_api"
    public const string PhoenixType    = "setup:phoenix_type"; // emulator type name
    public const string PhoenixConn    = "setup:phoenix_connstr";

    // Populated after a successful Phoenix API login -- used by the Plugin Store
    public const string PhoenixApiKey  = "phoenix:api_key";
    public const string PhoenixApiTier = "phoenix:api_tier";

    // Generated once by the setup wizard; written to agent-secrets.json and stored here as backup
    public const string AgentHmacKey   = "setup:agent_hmac_key";
}
