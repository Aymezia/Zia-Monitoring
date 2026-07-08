using System.Security.Cryptography;
using System.Text;
using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class ObsWebSocketServiceTests
{
    [Fact]
    public void ComputeAuthResponse_SuitLAlgorithmeDocumenteParObsWebSocketV5()
    {
        // Référence : secret = Base64(SHA256(password + salt))
        //             réponse = Base64(SHA256(secret + challenge))
        const string password = "hunter2";
        const string salt = "saltValue==";
        const string challenge = "challengeValue==";

        var expectedSecret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(expectedSecret + challenge)));

        var actual = ObsWebSocketService.ComputeAuthResponse(password, salt, challenge);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ComputeAuthResponse_EstDeterministe()
    {
        var first = ObsWebSocketService.ComputeAuthResponse("pw", "salt", "challenge");
        var second = ObsWebSocketService.ComputeAuthResponse("pw", "salt", "challenge");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeAuthResponse_MotDePasseDifferent_DonneUneReponseDifferente()
    {
        var a = ObsWebSocketService.ComputeAuthResponse("pw1", "salt", "challenge");
        var b = ObsWebSocketService.ComputeAuthResponse("pw2", "salt", "challenge");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputeAuthResponse_RetourneUneChaineBase64Valide()
    {
        var response = ObsWebSocketService.ComputeAuthResponse("pw", "salt", "challenge");

        Assert.NotEmpty(Convert.FromBase64String(response)); // ne lève pas si Base64 valide
    }
}
