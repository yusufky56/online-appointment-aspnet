using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace OnlineRandevuSistemi.Models
{
    public class Appointment
    {
        public int Id { get; set; }

        // Set by the controller from the logged-in user, never bound from the form.
        [BindNever]
        public string UserId { get; set; } = string.Empty;

        [BindNever]
        public IdentityUser? User { get; set; }

        [Required(ErrorMessage = "Randevu tarihi zorunludur.")]
        [Display(Name = "Randevu Tarihi")]
        public DateTime AppointmentDate { get; set; }

        [Required(ErrorMessage = "Lütfen bir hizmet seçin.")]
        [StringLength(100)]
        [Display(Name = "Hizmet Türü")]
        public string ServiceType { get; set; } = string.Empty;

        [StringLength(500)]
        [Display(Name = "Notlar")]
        [DisplayFormat(ConvertEmptyStringToNull = false)]
        public string Notes { get; set; } = string.Empty;

        [BindNever]
        [Display(Name = "Durum")]
        public AppointmentStatus Status { get; set; }
    }

    public enum AppointmentStatus
    {
        [Display(Name = "Beklemede")]
        Pending,
        [Display(Name = "Onaylandı")]
        Confirmed,
        [Display(Name = "İptal Edildi")]
        Cancelled,
        [Display(Name = "Tamamlandı")]
        Completed
    }

    public static class AppointmentStatusExtensions
    {
        public static string DisplayName(this AppointmentStatus status) =>
            typeof(AppointmentStatus).GetMember(status.ToString())[0]
                .GetCustomAttribute<DisplayAttribute>()?.Name ?? status.ToString();

        /// <summary>Bootstrap 5 background color for the status badge.</summary>
        public static string BadgeColor(this AppointmentStatus status) => status switch
        {
            AppointmentStatus.Confirmed => "success",
            AppointmentStatus.Pending => "warning text-dark",
            AppointmentStatus.Cancelled => "danger",
            _ => "info text-dark",
        };

        public static bool IsActive(this AppointmentStatus status) =>
            status is AppointmentStatus.Pending or AppointmentStatus.Confirmed;
    }
}
