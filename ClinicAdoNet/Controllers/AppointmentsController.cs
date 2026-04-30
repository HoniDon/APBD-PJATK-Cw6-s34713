using ClinicAdoNet.DTOs;
using ClinicAdoNet.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClinicAdoNet.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly AppointmentService _appointmentService;

    public AppointmentsController(AppointmentService appointmentService)
    {
        _appointmentService = appointmentService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        var appointments = await _appointmentService.GetAppointmentsAsync(status, patientLastName);
        return Ok(appointments);
    }
    
    [HttpGet("{idAppointment}")]
    public async Task<IActionResult> GetAppointmentById([FromRoute] int idAppointment)
    {
        var appointment = await _appointmentService.GetAppointmentDetailsAsync(idAppointment);

        if (appointment == null)
        {
            return NotFound(new ErrorResponseDto
            {
                Message = "Appointment not found."
            });
        }

        return Ok(appointment);
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto dto)
    {
        var result = await _appointmentService.CreateAppointmentAsync(dto);

        if (!result.Success)
        {
            if (result.ErrorMessage == "Doctor already has an appointment at this time.")
            {
                return Conflict(new ErrorResponseDto { Message = result.ErrorMessage! });
            }

            return BadRequest(new ErrorResponseDto { Message = result.ErrorMessage! });
        }

        return CreatedAtAction(
            nameof(GetAppointmentById),
            new { idAppointment = result.NewId },
            new { idAppointment = result.NewId }
        );
    }
}