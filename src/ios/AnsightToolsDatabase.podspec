Pod::Spec.new do |s|
  s.name         = "AnsightToolsDatabase"
  s.version      = "1.3.0-preview.12"
  s.summary      = "Ansight SQLite remote tools for native iOS apps"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "Ansight SDK Source-Available License", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = "Sources/AnsightToolsDatabase/**/*.swift"
  s.libraries    = "sqlite3"
  s.dependency "AnsightCore", s.version.to_s
  s.swift_version = "6.0"
end
