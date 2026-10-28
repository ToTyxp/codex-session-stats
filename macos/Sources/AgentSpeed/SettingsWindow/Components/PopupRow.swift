import SwiftUI

/// A pop-up menu trailing its row title, with an optional caption under the title as switch rows show theirs.
struct PopupRow<Selection: Hashable, Options: View>: View {
    let title: String
    var caption: String?
    @Binding var selection: Selection
    @ViewBuilder var options: Options

    var body: some View {
        LabeledContent {
            Picker(title, selection: $selection) {
                options
            }
            .pickerStyle(.menu)
            .labelsHidden()
            .fixedSize()
        } label: {
            VStack(alignment: .leading, spacing: 2) {
                RowTitle(title)
                if let caption {
                    Footnote(caption)
                }
            }
        }
    }
}
