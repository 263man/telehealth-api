namespace TelehealthApi.Core.Enums
{
    public enum AppointmentStatus
    {
        Proposed,          /// A proposed appointment, not yet agreed to by all participants.
        Pending,          /// The appointment is pending confirmation or action.
        Booked,           /// An appointment has been scheduled for a specific date and time.
        Arrived,          /// The patient has arrived and is waiting.
        Fulfilled,        /// The appointment has been completed.
        Cancelled,        /// The appointment has been cancelled by the patient or practitioner.
        Noshow,        /// The appointment was not completed (e.g., patient did not show up).
        InProgress,        /// The appointment is being processed (e.g., currently underway).
        Other,              // Useful for custom states not directly in FHIR, if needed. Other status not defined by the FHIR standard.
        CheckedIn,          // This is a common status in telehealth systems
        EnteredInError,     // Used when an appointment was entered incorrectly
        Waitlist,           // Indicates the appointment is in a queue or waiting list
        Unknown             // Useful if the FHIR status cannot be mapped or is missing        /// The status of the appointment is unknown.

    }
}
