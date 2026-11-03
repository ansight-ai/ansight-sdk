Pod::Spec.new do |s|
  s.name = "CAnsightWebP"
  s.version = "1.9.0"
  s.summary = "Pinned native WebP codec for Ansight offline captures"
  s.homepage = "https://github.com/ansight-ai/ansight-sdk"
  s.license = { :type => "BSD-3-Clause", :file => "Sources/CAnsightWebP/COPYING" }
  s.authors = { "Ansight" => "dev@ansight.ai" }
  s.source = { :path => "." }
  s.platforms = { :ios => "15.0", :osx => "10.15" }
  s.source_files = "Sources/CAnsightWebP/**/*.{c,h}"
  s.public_header_files = "Sources/CAnsightWebP/include/*.h"
  s.header_mappings_dir = "Sources/CAnsightWebP"
  s.pod_target_xcconfig = {
    "HEADER_SEARCH_PATHS" => '"$(PODS_TARGET_SRCROOT)/Sources/CAnsightWebP/vendor"',
    "GCC_PREPROCESSOR_DEFINITIONS" => "$(inherited) WEBP_USE_THREAD"
  }
end
