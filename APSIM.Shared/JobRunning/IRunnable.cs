﻿namespace APSIM.Shared.JobRunning
{
    /// <summary>A runnable interface.</summary>
    public interface IRunnable
    {
        /// <summary>
        /// Returns the job's progress as a real number in range [0, 1].
        /// </summary>
        double Progress { get; }

        /// <summary>
        /// Name of the job.
        /// </summary>
        string Name { get; }

        /// <summary>Called to start the job. Can throw on error.</summary>
        /// <param name="cancelToken">Is cancellation pending?</param>
        void Run(System.Threading.CancellationTokenSource cancelToken);
    }
}