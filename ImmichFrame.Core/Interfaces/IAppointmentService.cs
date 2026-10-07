using ImmichFrame.Core.Interfaces;

public interface ICalendarService
{
    public Task<List<IAppointment>> GetAppointments();
    public Task<List<IAppointment>> GetAppointments(DateTime from, DateTime to);
}