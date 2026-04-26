Clinic Appointments API

In this project I created a simple ASP.NET Core Web API for managing clinic appointments.

I connected the application to SQL Server using ADO.NET (SqlConnection, SqlCommand, SqlDataReader) without using Entity Framework.

I implemented the following endpoints:
- getting all appointments (with optional filters),
- getting details of one appointment,
- creating a new appointment,
- updating an existing appointment,
- deleting an appointment.

I used DTOs to return data instead of database models.

I added validation and business rules, for example:
- patient and doctor must exist and be active,
- appointment date cannot be in the past,
- doctor cannot have two appointments at the same time,
- completed appointments cannot be changed or deleted.

All SQL queries use parameters to prevent SQL Injection.
