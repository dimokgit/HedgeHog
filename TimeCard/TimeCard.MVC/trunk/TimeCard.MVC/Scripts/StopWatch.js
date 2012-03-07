var StopWatch =
	function () {
	  // Private vars
	  var startAt = 0; // Time of last start / resume. (0 if not running)
	  var lapTime = 0; // Time on the clock when last stopped in milliseconds

	  var now =
			function () {
			  return (new Date()).getTime();
			};

	  // Public methods
	  this.start = 	// Start or resume
			function () {
			  startAt = now();
			  return lapTime;
			}; // this.start

	  this.stop = // Stop or pause
			function () {
			  // If running, update elapsed time otherwise reset
			  lapTime = startAt ? lapTime + now() - startAt : 0;
			  startAt = 0; // Paused

			  return lapTime;
			}; // this.stop

	  this.time = // Duration
			function () {
			  return lapTime + (startAt ? now() - startAt : 0);
			}; // this.time
	}; // clsStopwatch

