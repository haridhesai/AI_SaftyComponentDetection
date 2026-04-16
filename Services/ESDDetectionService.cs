using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AI_COMPNENT_SAFTY.Services
{
    class ESDDetectionService
    {
        public string Status { get; private set; } = "NO PERSON";
        public int MailCount { get; private set; } = 0;
        public double ViolationTime { get; private set; } = 0;

        private DateTime? violationStartTime = null;
        private DateTime lastSeenWearing = DateTime.MinValue;
        private DateTime lastSeenNotWearing = DateTime.MinValue;

        private int warningCount = 0;
        private bool complaintMailSent = false;

        private const int VIOLATION_THRESHOLD = 10; // seconds
        private const int STATUS_HOLD_TIME = 3;     // seconds

        public void Process(int wearingCount, int notWearingCount)
        {
            DateTime now = DateTime.Now;

            double timeSinceWearing = (now - lastSeenWearing).TotalSeconds;
            double timeSinceNotWearing = (now - lastSeenNotWearing).TotalSeconds;

            // Update memory
            if (wearingCount > 0)
                lastSeenWearing = now;

            if (notWearingCount > 0)
                lastSeenNotWearing = now;

            // =============================
            // CONDITION 1: NO DETECTION
            // =============================
            if (wearingCount == 0 && notWearingCount == 0)
            {
                if (timeSinceWearing <= STATUS_HOLD_TIME)
                {
                    Status = "ESD_WEARING";
                }
                else if (timeSinceNotWearing <= STATUS_HOLD_TIME)
                {
                    Status = "VIOLATION";
                }
                else
                {
                    Status = "NO PERSON";
                    ResetViolation();
                }
            }

            // =============================
            // CONDITION 2: SAFE
            // =============================
            else if (wearingCount >= 1 && notWearingCount <= 1)
            {
                Status = "ESD_WEARING";
                ResetViolation();
            }

            // =============================
            // CONDITION 3: FULL VIOLATION
            // =============================
            else if (wearingCount == 0 && notWearingCount >= 1)
            {
                HandleViolation(now);
            }

            // =============================
            // CONDITION 4: MIXED CASE
            // =============================
            else if (wearingCount >= 1 && notWearingCount > 1)
            {
                HandleViolation(now);
            }
        }

        public void ResetViolationTimer()
        {
            violationStartTime = DateTime.Now;
        }

        private void HandleViolation(DateTime now)
        {
            Status = "VIOLATION";

            if (violationStartTime == null)
                violationStartTime = now;

            ViolationTime = (now - violationStartTime.Value).TotalSeconds;

            if (ViolationTime >= VIOLATION_THRESHOLD)
            {
                warningCount++;

                violationStartTime = now;

                if (warningCount >= 3 && !complaintMailSent)
                {
                    MailCount++;
                    complaintMailSent = true;
                    warningCount = 0;
                }
            }
        }

        private void ResetViolation()
        {
            violationStartTime = null;
            ViolationTime = 0;
            warningCount = 0;
            complaintMailSent = false;
        }
    }
}
