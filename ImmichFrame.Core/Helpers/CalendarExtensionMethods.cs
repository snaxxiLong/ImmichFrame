using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Models;

namespace ImmichFrame.WebApi.Helpers
{
    public static class CalendarExtensionMethods
    {
        public static IAppointment ToAppointment(this Occurrence occurrence)
        {
            if (occurrence.Source.GetType() == typeof(CalendarEvent)) {
                // For recurring events the master event carries the first start; use this occurrence's period.
                var appointment = ((CalendarEvent)occurrence.Source).ToAppointment();
                appointment.StartTime = occurrence.Period.StartTime.AsSystemLocal;
                appointment.EndTime = occurrence.Period.EndTime.AsSystemLocal;
                appointment.Duration = occurrence.Period.Duration;
                return appointment;
            }

            return new Appointment
            {
                //Summary = occurrence.Period.Duration.Summary,
                //Description = occurrence.Source.Description,
                StartTime = occurrence.Period.StartTime.AsSystemLocal,
                Duration = occurrence.Period.Duration,
                EndTime = occurrence.Period.EndTime.AsSystemLocal,
                Location = ""
            };
        }
        public static Appointment ToAppointment(this CalendarEvent calEvent)
        {
            return new Appointment
            {
                Summary = calEvent.Summary,
                Description = calEvent.Description,
                StartTime = calEvent.Start.AsSystemLocal,
                Duration = calEvent.Duration,
                EndTime = calEvent.End.AsSystemLocal,
                Location = calEvent.Location
            };
        }
    }
}
