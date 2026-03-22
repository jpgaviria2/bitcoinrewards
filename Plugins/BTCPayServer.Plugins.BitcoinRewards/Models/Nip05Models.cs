#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BTCPayServer.Plugins.BitcoinRewards.Models;

public class CheckUsernameResponse
{
    [JsonPropertyName("available")]
    public bool Available { get; set; }
    [JsonPropertyName("suggestion")]
    public string? Suggestion { get; set; }
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
    [JsonPropertyName("availableAfter")]
    public DateTime? AvailableAfter { get; set; }
}

public class ReleaseNip05Request
{
    [JsonPropertyName("walletId")]
    public Guid WalletId { get; set; }
}

public class UpdateUsernameRequest
{
    [JsonPropertyName("walletId")]
    public Guid WalletId { get; set; }
    [JsonPropertyName("newUsername")]
    public string NewUsername { get; set; } = string.Empty;
}

public class NostrJsonResponse
{
    [JsonPropertyName("names")]
    public Dictionary<string, string> Names { get; set; } = new();
    [JsonPropertyName("relays")]
    public Dictionary<string, string[]>? Relays { get; set; }
}

public class Nip05UserDto
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    [JsonPropertyName("pubkey")]
    public string Pubkey { get; set; } = string.Empty;
    [JsonPropertyName("revoked")]
    public bool Revoked { get; set; }
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty; // "wallet" or "standalone"
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

public class RevokeRestoreRequest
{
    [JsonPropertyName("pubkey")]
    public string Pubkey { get; set; } = string.Empty;
}

public class Nip05LookupResponse
{
    [JsonPropertyName("walletId")]
    public Guid? WalletId { get; set; }
    [JsonPropertyName("nip05")]
    public string Nip05 { get; set; } = string.Empty;
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
    [JsonPropertyName("revoked")]
    public bool Revoked { get; set; }
}
