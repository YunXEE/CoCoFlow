using System;
using System.Threading;
using CoCoFlow.Runtime.Animation.Contracts;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Modules.Animation.UniTask
{
    public static class AnimPlaybackUniTaskExtensions
    {
        /// <summary>
        /// Waits until one currently published playback token completes or is interrupted.
        /// Cancelling the token cancels only this waiter and never changes playback.
        /// </summary>
        public static async UniTask<AnimPlaybackStatus> WaitForTerminalStatusAsync(
            this AnimOperator animOperator,
            AnimPlaybackToken playbackToken,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (animOperator == null)
            {
                throw new ArgumentNullException(nameof(animOperator));
            }

            if (!playbackToken.IsValid)
            {
                throw new ArgumentException(
                    "A valid AnimPlaybackToken is required.",
                    nameof(playbackToken));
            }

            if (!animOperator.TryGetPlayback(
                    playbackToken.Layer,
                    out AnimPlaybackLayer playback) ||
                playback.Token != playbackToken)
            {
                throw new InvalidOperationException(
                    "AnimOperator does not currently publish the supplied playback token.");
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (animOperator == null)
                {
                    throw new InvalidOperationException(
                        "AnimOperator was destroyed while awaiting playback.");
                }

                if (!animOperator.TryGetPlayback(
                        playbackToken.Layer,
                        out playback))
                {
                    throw new InvalidOperationException(
                        "AnimOperator no longer exposes the playback token's layer.");
                }

                if (playback.Token != playbackToken)
                {
                    return AnimPlaybackStatus.Interrupted;
                }

                if (playback.Status == AnimPlaybackStatus.Completed ||
                    playback.Status == AnimPlaybackStatus.Interrupted)
                {
                    return playback.Status;
                }

                if (!playback.IsActive)
                {
                    throw new InvalidOperationException(
                        "AnimOperator published a non-terminal inactive playback token.");
                }

                await Cysharp.Threading.Tasks.UniTask.Yield(
                    PlayerLoopTiming.Update,
                    cancellationToken);
            }
        }
    }
}
