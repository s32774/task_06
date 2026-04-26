using APBD_TASK6.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;
using APBD_TASK6.Exceptions;

namespace APBD_TASK6.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly string _connectionString;

    public AppointmentsController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing 'DefaultConnection' in appsettings.json.");
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName)
    {
        var appointments = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

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

        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrWhiteSpace(status) ? DBNull.Value : status;

        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value =
            string.IsNullOrWhiteSpace(patientLastName) ? DBNull.Value : patientLastName;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }

        return Ok(appointments);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetAppointmentById(int idAppointment)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

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
                p.PhoneNumber AS PatientPhoneNumber,
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

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return NotFound(new { message = "Appointment not found." });
        }

        var appointment = new AppointmentDetailsDto
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
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),

            IdDoctor = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
            DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            SpecializationName = reader.GetString(reader.GetOrdinal("SpecializationName"))
        };

        return Ok(appointment);
    }
    [HttpPost]
public async Task<IActionResult> CreateAppointment(CreateAppointmentRequestDto request)
{
    try { 
    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        return BadRequest(new { message = "Reason is required and must be <= 250 characters." });

    if (request.AppointmentDate < DateTime.UtcNow)
        return BadRequest(new { message = "Appointment date cannot be in the past." });

    await using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync();

    // check patient
    await using (var checkPatient = new SqlCommand(
        "SELECT COUNT(1) FROM dbo.Patients WHERE IdPatient = @Id AND IsActive = 1", connection))
    {
        checkPatient.Parameters.Add("@Id", SqlDbType.Int).Value = request.IdPatient;
        var exists = (int)await checkPatient.ExecuteScalarAsync();
        if (exists == 0)
            return BadRequest(new { message = "Patient does not exist or is inactive." });
    }

    // check doctor
    await using (var checkDoctor = new SqlCommand(
        "SELECT COUNT(1) FROM dbo.Doctors WHERE IdDoctor = @Id AND IsActive = 1", connection))
    {
        checkDoctor.Parameters.Add("@Id", SqlDbType.Int).Value = request.IdDoctor;
        var exists = (int)await checkDoctor.ExecuteScalarAsync();
        if (exists == 0)
            return BadRequest(new { message = "Doctor does not exist or is inactive." });
    }

    // check conflict
    await using (var checkConflict = new SqlCommand(
        """
        SELECT COUNT(1)
        FROM dbo.Appointments
        WHERE IdDoctor = @IdDoctor
          AND AppointmentDate = @Date
          AND Status = 'Scheduled'
        """, connection))
    {
        checkConflict.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
        checkConflict.Parameters.Add("@Date", SqlDbType.DateTime2).Value = request.AppointmentDate;

        var conflict = (int)await checkConflict.ExecuteScalarAsync();
        if (conflict > 0)
            throw new AppointmentConflictException("Doctor already has an appointment at this time.");
    }

    // insert
    await using var command = new SqlCommand(
        """
        INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
        VALUES (@IdPatient, @IdDoctor, @Date, 'Scheduled', @Reason);

        SELECT SCOPE_IDENTITY();
        """, connection);

    command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
    command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
    command.Parameters.Add("@Date", SqlDbType.DateTime2).Value = request.AppointmentDate;
    command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;

    var newId = Convert.ToInt32(await command.ExecuteScalarAsync());

    return Created($"/api/appointments/{newId}", new { id = newId });
}
    catch (AppointmentConflictException ex)
    {
        return Conflict(new { message = ex.Message });
    }
}
[HttpPut("{idAppointment:int}")]
public async Task<IActionResult> UpdateAppointment(int idAppointment, UpdateAppointmentRequestDto request)
{
    try { 
    if (!new[] { "Scheduled", "Completed", "Cancelled" }.Contains(request.Status))
        return BadRequest(new { message = "Invalid status." });

    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        return BadRequest(new { message = "Reason must be <= 250 characters." });

    await using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync();

    string currentStatus;
    DateTime currentDate;

    await using (var checkCmd = new SqlCommand(
        "SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @Id", connection))
    {
        checkCmd.Parameters.Add("@Id", SqlDbType.Int).Value = idAppointment;

        await using var reader = await checkCmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return NotFound(new { message = "Appointment not found." });

        currentStatus = reader.GetString(0);
        currentDate = reader.GetDateTime(1);
    }

    if (currentStatus == "Completed" && request.AppointmentDate != currentDate)
        return Conflict(new { message = "Cannot change date of completed appointment." });

    await using (var checkPatient = new SqlCommand(
        "SELECT COUNT(1) FROM dbo.Patients WHERE IdPatient = @Id AND IsActive = 1", connection))
    {
        checkPatient.Parameters.Add("@Id", SqlDbType.Int).Value = request.IdPatient;
        if ((int)await checkPatient.ExecuteScalarAsync() == 0)
            return BadRequest(new { message = "Patient invalid." });
    }

    await using (var checkDoctor = new SqlCommand(
        "SELECT COUNT(1) FROM dbo.Doctors WHERE IdDoctor = @Id AND IsActive = 1", connection))
    {
        checkDoctor.Parameters.Add("@Id", SqlDbType.Int).Value = request.IdDoctor;
        if ((int)await checkDoctor.ExecuteScalarAsync() == 0)
            return BadRequest(new { message = "Doctor invalid." });
    }

    await using (var conflictCmd = new SqlCommand(
        """
        SELECT COUNT(1)
        FROM dbo.Appointments
        WHERE IdDoctor = @Doc
          AND AppointmentDate = @Date
          AND IdAppointment <> @Id
          AND Status = 'Scheduled'
        """, connection))
    {
        conflictCmd.Parameters.Add("@Doc", SqlDbType.Int).Value = request.IdDoctor;
        conflictCmd.Parameters.Add("@Date", SqlDbType.DateTime2).Value = request.AppointmentDate;
        conflictCmd.Parameters.Add("@Id", SqlDbType.Int).Value = idAppointment;

        if ((int)await conflictCmd.ExecuteScalarAsync() > 0)
            throw new AppointmentConflictException("Doctor has another appointment at this time.");
    }
    
    await using var command = new SqlCommand(
        """
        UPDATE dbo.Appointments
        SET IdPatient = @IdPatient,
            IdDoctor = @IdDoctor,
            AppointmentDate = @Date,
            Status = @Status,
            Reason = @Reason,
            InternalNotes = @Notes
        WHERE IdAppointment = @Id
        """, connection);

    command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = request.IdPatient;
    command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = request.IdDoctor;
    command.Parameters.Add("@Date", SqlDbType.DateTime2).Value = request.AppointmentDate;
    command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = request.Status;
    command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = request.Reason;
    command.Parameters.Add("@Notes", SqlDbType.NVarChar, 500).Value =
        (object?)request.InternalNotes ?? DBNull.Value;
    command.Parameters.Add("@Id", SqlDbType.Int).Value = idAppointment;

    await command.ExecuteNonQueryAsync();

    return Ok(new { message = "Updated successfully." });
}
    catch (AppointmentConflictException ex)
    {
        return Conflict(new { message = ex.Message });
    }
}
[HttpDelete("{idAppointment:int}")]
public async Task<IActionResult> DeleteAppointment(int idAppointment)
{
    await using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync();

    string status;

    await using (var checkCommand = new SqlCommand(
                     "SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment",
                     connection))
    {
        checkCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

        var result = await checkCommand.ExecuteScalarAsync();

        if (result is null)
            return NotFound(new { message = "Appointment not found." });

        status = result.ToString()!;
    }

    if (status == "Completed")
        return Conflict(new { message = "Completed appointments cannot be deleted." });

    await using var deleteCommand = new SqlCommand(
        "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment",
        connection);

    deleteCommand.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

    await deleteCommand.ExecuteNonQueryAsync();

    return NoContent();
}
}