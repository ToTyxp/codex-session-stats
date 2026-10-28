import Foundation

/// Where each orb goes when the display arrangement changes (DESIGN 2b, Arrangement changes).
public enum OrbLayoutResolver {
    /// One entry per orb (by id): the new arrangement's layout when it has one on a connected display; else the
    /// position from the previous arrangement when its display is still connected; else the orb's last position on a
    /// connected display (the main display first); else its last record moved to the main display, keeping the
    /// anchors. Nil only for an orb that never had a position (the app places it by the creation rule or the default
    /// corner). Overlaps are resolved afterwards by the app, in list order.
    public static func entries(
        document: OrbsDocument,
        arrangement: String,
        previousArrangement: String?,
        connected: [String],
        main: String
    ) -> [String: OrbLayoutEntry?] {
        let connectedSet = Set(connected)
        var result: [String: OrbLayoutEntry?] = [:]
        for orb in document.orbs {
            result[orb.id] = entry(for: orb.id, document: document, arrangement: arrangement,
                                   previousArrangement: previousArrangement, connected: connectedSet, main: main)
        }
        return result
    }

    static func entry(
        for id: String,
        document: OrbsDocument,
        arrangement: String,
        previousArrangement: String?,
        connected: Set<String>,
        main: String
    ) -> OrbLayoutEntry? {
        if let entry = document.entry(orb: id, arrangement: arrangement), connected.contains(entry.display) {
            return entry
        }
        if let previousArrangement, let entry = document.entry(orb: id, arrangement: previousArrangement),
           connected.contains(entry.display) {
            return entry
        }
        let records = document.displayRecords[id] ?? [:]
        if let record = records[main] {
            return OrbLayoutEntry(display: main, record: record)
        }
        if let display = records.keys.filter(connected.contains).sorted().first, let record = records[display] {
            return OrbLayoutEntry(display: display, record: record)
        }
        let previous = previousArrangement.flatMap { document.entry(orb: id, arrangement: $0) }
        if let record = previous?.record ?? document.entry(orb: id, arrangement: arrangement)?.record
            ?? records.keys.sorted().first.flatMap({ records[$0] }) {
            return OrbLayoutEntry(display: main, record: record)
        }
        return nil
    }
}
