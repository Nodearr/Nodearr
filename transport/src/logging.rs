//! Plain, one-event-per-line stderr for Nodearr to relay at the original level.

use std::fmt;

use tracing::{Event, Subscriber};
use tracing_subscriber::{
    fmt::{format, FmtContext, FormatEvent, FormatFields},
    registry::LookupSpan,
};

pub(crate) struct SingleLine;

impl<S, N> FormatEvent<S, N> for SingleLine
where
    S: Subscriber + for<'a> LookupSpan<'a>,
    N: for<'a> FormatFields<'a> + 'static,
{
    fn format_event(
        &self,
        ctx: &FmtContext<'_, S, N>,
        mut writer: format::Writer<'_>,
        event: &Event<'_>,
    ) -> fmt::Result {
        let mut line = String::new();
        format().without_time().with_ansi(false).format_event(
            ctx,
            format::Writer::new(&mut line),
            event,
        )?;

        // Upstream Debug/Display implementations can contain newlines even without {:#?}.
        // Escape them after formatting the entire event, including its span and fields.
        for character in line.trim_end_matches(['\r', '\n']).chars() {
            match character {
                '\n' => writer.write_str("\\n")?,
                '\r' => writer.write_str("\\r")?,
                '\t' => writer.write_str("\\t")?,
                character if character.is_control() => {
                    write!(writer, "\\u{:04x}", character as u32)?;
                }
                character => writer.write_char(character)?,
            }
        }

        writeln!(writer)
    }
}

#[cfg(test)]
mod tests {
    use std::{
        fmt, io,
        sync::{Arc, Mutex},
    };

    use super::SingleLine;

    #[derive(Clone, Default)]
    struct Buffer(Arc<Mutex<Vec<u8>>>);

    impl io::Write for Buffer {
        fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
            self.0.lock().unwrap().extend_from_slice(bytes);
            Ok(bytes.len())
        }

        fn flush(&mut self) -> io::Result<()> {
            Ok(())
        }
    }

    struct NoRoute;

    impl fmt::Debug for NoRoute {
        fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
            formatter.write_str("NoRoute {\n    meta: Meta,\n}")
        }
    }

    #[test]
    fn events_are_plain_single_lines_with_levels_targets_and_fields_but_no_time() {
        let buffer = Buffer::default();
        let writer = buffer.clone();
        let subscriber = tracing_subscriber::fmt()
            .with_ansi(false)
            .event_format(SingleLine)
            .with_max_level(tracing::Level::TRACE)
            .with_writer(move || writer.clone())
            .finish();

        tracing::subscriber::with_default(subscriber, || {
            tracing::error!(target: "nodearr_transport", "error message");
            tracing::warn!(target: "netwatch::interfaces::windows", error = ?NoRoute, "failed to retrieve default route");
            tracing::info!(target: "nodearr_transport", "info message");
            tracing::debug!(target: "nodearr_transport", "debug message");
            let span = tracing::info_span!("request", peer = "first\nsecond");
            let _guard = span.enter();
            tracing::trace!(target: "nodearr_transport", "message\r\nwith\tescape\u{1b}[33m");
        });

        let output = String::from_utf8(buffer.0.lock().unwrap().clone()).unwrap();
        let lines: Vec<_> = output.lines().collect();
        assert_eq!(lines.len(), 5, "{output}");
        for (line, level) in lines
            .iter()
            .zip(["ERROR", "WARN", "INFO", "DEBUG", "TRACE"])
        {
            assert!(
                line.trim_start().starts_with(&format!("{level} ")),
                "{line}"
            );
            assert!(!line.contains(['\u{1b}', '\r', '\t']), "{line}");
        }
        assert_eq!(lines[0], "ERROR nodearr_transport: error message");
        assert_eq!(lines[1], " WARN netwatch::interfaces::windows: failed to retrieve default route error=NoRoute {\\n    meta: Meta,\\n}");
        // tracing escapes an ESC in a message before our formatter sees it.
        assert!(
            lines[4].contains("message\\r\\nwith\\tescape\\x1b[33m"),
            "{}",
            lines[4]
        );
        assert!(lines[4].contains("request{peer="));
        assert!(output.ends_with('\n'));
    }
}
