using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineRandevuSistemi.Data;
using OnlineRandevuSistemi.Models;

namespace OnlineRandevuSistemi.Controllers
{
    [Authorize]
    public class AppointmentController : Controller
    {
        private static readonly TimeSpan OpeningTime = new(9, 0, 0);
        private static readonly TimeSpan ClosingTime = new(18, 0, 0);
        private static readonly TimeSpan SlotLength = TimeSpan.FromHours(1);

        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public AppointmentController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        private string CurrentUserId => _userManager.GetUserId(User)!;

        public async Task<IActionResult> Index()
        {
            var appointments = await _context.Appointments
                .Where(a => a.UserId == CurrentUserId)
                .OrderByDescending(a => a.AppointmentDate)
                .ToListAsync();

            return View(appointments);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Appointment appointment)
        {
            if (appointment.AppointmentDate <= DateTime.Now)
            {
                ModelState.AddModelError(nameof(Appointment.AppointmentDate), "Geçmiş bir tarihe randevu alınamaz.");
            }
            else if (appointment.AppointmentDate.TimeOfDay < OpeningTime ||
                     appointment.AppointmentDate.TimeOfDay + SlotLength > ClosingTime)
            {
                ModelState.AddModelError(nameof(Appointment.AppointmentDate), "Randevular 09:00 - 18:00 arasında alınabilir.");
            }
            else if (await IsSlotTakenAsync(appointment.AppointmentDate))
            {
                ModelState.AddModelError(nameof(Appointment.AppointmentDate), "Bu saat dolu, lütfen başka bir saat seçin.");
            }

            if (!ModelState.IsValid)
            {
                return View(appointment);
            }

            appointment.UserId = CurrentUserId;
            appointment.Status = AppointmentStatus.Pending;
            _context.Add(appointment);
            await _context.SaveChangesAsync();

            TempData["Message"] = "Randevunuz oluşturuldu, onay bekliyor.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            // Only the owner can cancel; other users get 404 so ids can't be probed.
            var appointment = await _context.Appointments
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == CurrentUserId);
            if (appointment == null)
            {
                return NotFound();
            }

            if (appointment.Status.IsActive())
            {
                appointment.Status = AppointmentStatus.Cancelled;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Calendar feed. Users only see their own appointments.</summary>
        [HttpGet]
        public async Task<JsonResult> GetAppointments(DateTime start, DateTime end)
        {
            var appointments = await _context.Appointments
                .Where(a => a.UserId == CurrentUserId && a.AppointmentDate >= start && a.AppointmentDate <= end)
                .Select(a => new
                {
                    id = a.Id,
                    title = a.ServiceType,
                    start = a.AppointmentDate,
                    end = a.AppointmentDate.AddHours(1),
                    status = a.Status.ToString()
                })
                .ToListAsync();

            return Json(appointments);
        }

        private Task<bool> IsSlotTakenAsync(DateTime start)
        {
            var end = start.Add(SlotLength);
            return _context.Appointments.AnyAsync(a =>
                (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed) &&
                a.AppointmentDate < end && start < a.AppointmentDate.AddHours(1));
        }
    }
}
