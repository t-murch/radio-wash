using System.ComponentModel.DataAnnotations;

namespace RadioWash.Api.Models.Requests;

public class SignUpRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = null!;

    [Required]
    public string DisplayName { get; set; } = null!;
}