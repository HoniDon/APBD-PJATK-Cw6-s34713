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
    
    public async Task<AppointmentDetailsDto?> GetAppointmentDetailsAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);

        await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,

                p.IdPatient,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail,
                p.PhoneNumber AS PatientPhone,

                d.IdDoctor,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber AS DoctorLicenseNumber,
                s.Name AS SpecializationName
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            JOIN dbo.Doctors d ON d.IdDoctor = a.IdDoctor
            JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
            """, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await connection.OpenAsync();

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes"))
                ? null
                : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),

            IdPatient = reader.GetInt32(reader.GetOrdinal("IdPatient")),
            PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhone = reader.GetString(reader.GetOrdinal("PatientPhone")),

            IdDoctor = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
            DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            SpecializationName = reader.GetString(reader.GetOrdinal("SpecializationName"))
        };
    }
    
    private async Task<bool> PatientExistsAndIsActiveAsync(SqlConnection connection, int idPatient)
    {
        await using var command = new SqlCommand("""
                                                 SELECT COUNT(1)
                                                 FROM dbo.Patients
                                                 WHERE IdPatient = @IdPatient AND IsActive = 1;
                                                 """, connection);

        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = idPatient;

        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }

    private async Task<bool> DoctorExistsAndIsActiveAsync(SqlConnection connection, int idDoctor)
    {
        await using var command = new SqlCommand("""
                                                 SELECT COUNT(1)
                                                 FROM dbo.Doctors
                                                 WHERE IdDoctor = @IdDoctor AND IsActive = 1;
                                                 """, connection);

        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = idDoctor;

        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }

    private async Task<bool> DoctorHasConflictAsync(SqlConnection connection, int idDoctor, DateTime appointmentDate, int? ignoreAppointmentId = null)
    {
        var sql = """
                  SELECT COUNT(1)
                  FROM dbo.Appointments
                  WHERE IdDoctor = @IdDoctor
                    AND AppointmentDate = @AppointmentDate
                    AND Status = 'Scheduled'
                  """;

        if (ignoreAppointmentId.HasValue)
        {
            sql += " AND IdAppointment <> @IgnoreAppointmentId";
        }

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = idDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = appointmentDate;

        if (ignoreAppointmentId.HasValue)
        {
            command.Parameters.Add("@IgnoreAppointmentId", SqlDbType.Int).Value = ignoreAppointmentId.Value;
        }

        var count = (int)await command.ExecuteScalarAsync();
        return count > 0;
    }
}