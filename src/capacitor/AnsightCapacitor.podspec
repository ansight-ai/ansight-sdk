Pod::Spec.new do |s|
  s.name = 'AnsightCapacitor'
  s.version = '1.2.0-preview.3'
  s.summary = 'Capacitor bridge for the Ansight mobile observability SDK.'
  s.license = { :type => 'Ansight SDK Source-Available License', :file => 'LICENSE' }
  s.homepage = 'https://github.com/ansight-ai/ansight-sdk'
  s.author = { 'Ansight' => 'hello@ansight.ai' }
  s.source = { :git => 'https://github.com/ansight-ai/ansight-sdk.git', :tag => s.version.to_s }
  s.source_files = 'ios/Sources/AnsightCapacitorPlugin/**/*.{swift,h,m,c,cc,mm,cpp}'
  s.ios.deployment_target = '15.0'
  s.swift_version = '5.9'
  s.dependency 'Capacitor'
  s.dependency 'Ansight', s.version.to_s
end
