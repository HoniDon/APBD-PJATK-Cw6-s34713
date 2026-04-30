using System.Data;
using ClinicAdoNet.DTOs;
using Microsoft.Data.SqlClient;

namespace ClinicAdoNet.Services;

public class AppointmentService
{
    private readonly string _connectionString;

    public AppointmentService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
                            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");
    }
    
    public async Task<List<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName)
    {
        var result = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);

        await using var command = new SqlCommand("""
                                                 SELECT
                                                     a.IdAppointment,
                                                     a.AppointmentDate,
                                                     a.Status,
                                                     a.Reason,
                                                     p.FirstName + N' ' + p.LastName AS PatientFullName,
                                                     p.Email AS PatientEmail
                                                 FROM dbo.Appointments a
                                                 JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                                                 WHERE (@Status IS NULL OR a.Status = @Status)
                                                   AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
                                                 ORDER BY a.AppointmentDate;
                                                 """, connection);

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value =
            string.IsNullOrWhiteSpace(status) ? DBNull.Value : status;

        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 100).Value =
            string.IsNullOrWhiteSpace(patientLastName) ? DBNull.Value : patientLastName;

        await connection.OpenAsync();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return result;
    }
}