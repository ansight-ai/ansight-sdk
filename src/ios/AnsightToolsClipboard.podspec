Pod::Spec.new do |s|
  s.name         = "AnsightToolsClipboard"
  s.version      = "1.5.0"
  s.summary      = "Ansight clipboard remote tools for native iOS apps"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "Ansight SDK Source-Available License", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = "Sources/AnsightToolsClipboard/**/*.swift"
  s.dependency "AnsightCore", s.version.to_s
  s.swift_version = "6.0"
end
