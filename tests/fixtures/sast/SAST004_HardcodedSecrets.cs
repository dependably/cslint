namespace Fixtures.Sast;

public class HardcodedSecretsFixture
{
    private string _password = "changeme";
    private string _apiKey = "sk-abc123secretkey";

    public void Connect()
    {
        string connectionString = "Server=localhost;Password=admin;";
        string token = "Bearer eyJhbGc...";
    }
}
