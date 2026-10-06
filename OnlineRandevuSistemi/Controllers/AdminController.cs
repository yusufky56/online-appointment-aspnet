using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineRandevuSistemi.Data;
using OnlineRandevuSistemi.Models;

namespace OnlineRandevuSistemi.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var appointments = await _context.Appointments
                .Include(a => a.User)
                .OrderByDescending(a => a.AppointmentDate)
                .ToListAsync();

            return View(appointments);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> ConfirmAppointment(int id) => SetStatus(id, AppointmentStatus.Confirmed);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> CancelAppointment(int id) => SetStatus(id, AppointmentStatus.Cancelled);

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> CompleteAppointment(int id) => SetStatus(id, AppointmentStatus.Completed);

        private async Task<IActionResult> SetStatus(int id, AppointmentStatus status)
        {
            var appointment = await _context.Appointments.FindAsync(id);
            if (appointment == null)
            {
                return NotFound();
            }

            appointment.Status = status;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
