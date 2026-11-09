// SPDX-License-Identifier: Apache-2.0
// P3-00: source-file execution uses only Java 17 standard-library APIs.
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Base64;
import java.util.StringJoiner;
import java.util.regex.Pattern;

class CheckPatterns {
    private static String decode(String value) {
        return new String(Base64.getDecoder().decode(value), StandardCharsets.UTF_8);
    }

    public static void main(String[] args) throws Exception {
        int checked = 0;
        for (String line : Files.readAllLines(Path.of(args[0]), StandardCharsets.UTF_8)) {
            if (line.startsWith("#")) continue;
            String[] fields = line.split("\t", -1);
            String pattern = decode(fields[1]);
            var matcher = Pattern.compile(pattern).matcher(decode(fields[2]));
            var actual = new StringJoiner(",");
            boolean emits = pattern.contains("(?<emit>");
            while (matcher.find()) {
                int start = emits ? matcher.start("emit") : matcher.start();
                int end = emits ? matcher.end("emit") : matcher.end();
                actual.add(matcher.start() + ":" + (matcher.end() - matcher.start()) + ":" + start + ":" + (end - start));
            }
            if (!actual.toString().equals(fields[3])) {
                throw new IllegalStateException("Regex portability differs for " + fields[0]);
            }
            checked++;
        }
        if (checked == 0) throw new IllegalStateException("No portability cases were measured");
        System.out.println("JVM regex portability passed: " + checked + " cases.");
    }
}
