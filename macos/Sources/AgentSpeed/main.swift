import AppKit

let options = LaunchOptions.parse()
let application = NSApplication.shared
let delegate = AppDelegate(options: options)
application.delegate = delegate
application.setActivationPolicy(options.previewDirectory == nil ? .accessory : .prohibited)
application.run()
