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
    
    public async Task<(bool Success, string? ErrorMessage, int? NewId)> CreateAppointmentAsync(CreateAppointmentRequestDto dto)
    {
        if (dto.AppointmentDate < DateTime.Now)
        {
            return (false, "Appointment date cannot be in the past.", null);
        }

        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return (false, "Reason is required.", null);
        }

        if (dto.Reason.Length > 250)
        {
            return (false, "Reason cannot be longer than 250 characters.", null);
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        if (!await PatientExistsAndIsActiveAsync(connection, dto.IdPatient))
        {
            return (false, "Patient does not exist or is not active.", null);
        }

        if (!await DoctorExistsAndIsActiveAsync(connection, dto.IdDoctor))
        {
            return (false, "Doctor does not exist or is not active.", null);
        }

        if (await DoctorHasConflictAsync(connection, dto.IdDoctor, dto.AppointmentDate))
        {
            return (false, "Doctor already has an appointment at this time.", null);
        }

        await using var command = new SqlCommand("""
                                                 INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason, CreatedAt)
                                                 OUTPUT INSERTED.IdAppointment
                                                 VALUES (@IdPatient, @IdDoctor, @AppointmentDate, @Status, @Reason, SYSUTCDATETIME());
                                                 """, connection);

        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = "Scheduled";
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;

        var newId = (int)await command.ExecuteScalarAsync();

        return (true, null, newId);
    }
    
    private async Task<AppointmentDetailsDto?> GetAppointmentForUpdateAsync(SqlConnection connection, int idAppointment)
    {
        await using var command = new SqlCommand("""
                                                 SELECT
                                                     a.IdAppointment,
                                                     a.AppointmentDate,
                                                     a.Status
                                                 FROM dbo.Appointments a
                                                 WHERE a.IdAppointment = @IdAppointment;
                                                 """, connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status"))
        };
    }
    
    public async Task<(bool Success, string? ErrorMessage, bool NotFound)> UpdateAppointmentAsync(int idAppointment, UpdateAppointmentRequestDto dto)
    {
        var allowedStatuses = new[] { "Scheduled", "Completed", "Cancelled" };

        if (!allowedStatuses.Contains(dto.Status))
        {
            return (false, "Invalid status value.", false);
        }

        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return (false, "Reason is required.", false);
        }

        if (dto.Reason.Length > 250)
        {
            return (false, "Reason cannot be longer than 250 characters.", false);
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using (var checkCommand = new SqlCommand("""
            SELECT IdAppointment, Status
            FROM dbo.Appointments
            WHERE IdAppointment = @IdAppointment;
            """, connection))
        {
            checkCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

            await using var reader = await checkCommand.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return (false, "Appointment not found.", true);
            }

            var currentStatus = reader.GetString(reader.GetOrdinal("Status"));

            if (currentStatus == "Completed" && dto.AppointmentDate != default)
            {
                return (false, "Completed appointment cannot change the date.", false);
            }
        }

        if (!await PatientExistsAndIsActiveAsync(connection, dto.IdPatient))
        {
            return (false, "Patient does not exist or is not active.", false);
        }

        if (!await DoctorExistsAndIsActiveAsync(connection, dto.IdDoctor))
        {
            return (false, "Doctor does not exist or is not active.", false);
        }

        if (await DoctorHasConflictAsync(connection, dto.IdDoctor, dto.AppointmentDate, idAppointment))
        {
            return (false, "Doctor already has an appointment at this time.", false);
        }

        await using var updateCommand = new SqlCommand("""
            UPDATE dbo.Appointments
            SET
                IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
            """, connection);

        updateCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;
        updateCommand.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
        updateCommand.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
        updateCommand.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
        updateCommand.Parameters.Add("@Status", SqlDbType.NVarChar, 50).Value = dto.Status;
        updateCommand.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;
        updateCommand.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, -1).Value =
            string.IsNullOrWhiteSpace(dto.InternalNotes) ? DBNull.Value : dto.InternalNotes;

        await updateCommand.ExecuteNonQueryAsync();

        return (true, null, false);
    }
    
    public async Task<(bool Success, string? ErrorMessage, bool NotFound)> DeleteAppointmentAsync(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        string? status = null;

        await using (var checkCommand = new SqlCommand("""
                                                       SELECT Status
                                                       FROM dbo.Appointments
                                                       WHERE IdAppointment = @IdAppointment;
                                                       """, connection))
        {
            checkCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

            await using var reader = await checkCommand.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return (false, "Appointment not found.", true);
            }

            status = reader.GetString(reader.GetOrdinal("Status"));
        }

        if (status == "Completed")
        {
            return (false, "Completed appointment cannot be deleted.", false);
        }

        await using var deleteCommand = new SqlCommand("""
                                                       DELETE FROM dbo.Appointments
                                                       WHERE IdAppointment = @IdAppointment;
                                                       """, connection);

        deleteCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        await deleteCommand.ExecuteNonQueryAsync();

        return (true, null, false);
    }
}