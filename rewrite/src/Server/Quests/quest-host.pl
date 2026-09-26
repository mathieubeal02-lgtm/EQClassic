#!/usr/bin/perl
# Runs one event of a legacy quest script (quests/<zone>/<npc>.pl) for the rewrite server.
# The server writes the variables on standard input ("name<TAB>value" lines, then "itemcount<TAB>id<TAB>n"),
# and the arguments are: quest file, event name (EVENT_SAY, EVENT_ITEM...), plugins directory.
# Every quest:: call the script makes comes back as a line "function<TAB>arg<TAB>arg..." on standard
# output, for the server to apply; the script itself changes nothing in the world.
use strict;
use warnings;
no warnings qw(redefine once uninitialized);

use File::Spec;
my ($file, $event, $plugins) = @ARGV;
$file = File::Spec->rel2abs($file);
$plugins = File::Spec->rel2abs($plugins) if $plugins;

sub emit {
    my @fields = map { my $v = defined $_ ? "$_" : ''; $v =~ s/[\t\r\n]/ /g; $v } @_;
    print join("\t", @fields), "\n";
}

package quest;
our $AUTOLOAD;
sub AUTOLOAD {
    my $name = $AUTOLOAD;
    $name =~ s/.*:://;
    return if $name eq 'DESTROY';
    main::emit($name, @_);
    return 0;
}

# Discipline tomes came with Luclin: no item is one on a Trilogy server.
sub isdisctome { return 0; }

package plugin;
our $AUTOLOAD;
sub AUTOLOAD {
    my $name = $AUTOLOAD;
    $name =~ s/.*:://;
    return if $name eq 'DESTROY';
    main::emit("plugin_$name", @_);
    return 0;
}

package main;
no strict 'vars';

while (my $line = <STDIN>) {
    chomp $line;
    my @f = split /\t/, $line, -1;
    next unless @f >= 2;
    if ($f[0] eq 'itemcount') {
        $main::itemcount{$f[1]} = $f[2];
    } else {
        no strict 'refs';
        ${"main::$f[0]"} = $f[1];
    }
}

if ($plugins && -d $plugins) {
    opendir(my $dir, $plugins);
    for my $plugin (sort grep { /\.pl$/ } readdir($dir)) {
        eval { package plugin; do "$plugins/$plugin"; };
    }
    closedir($dir);
}

# plugin::check_handin, without the debug lines of the copy in some quest packs: true when every
# required item was handed in, in the required numbers (they are then taken from %itemcount).
package plugin;
sub check_handin {
    my $hashref = shift;
    my %required = @_;
    foreach my $req (keys %required) {
        return 0 if (!defined $hashref->{$req}) || ($hashref->{$req} < $required{$req});
    }
    foreach my $req (keys %required) {
        $hashref->{$req} -= $required{$req};
        delete $hashref->{$req} if $hashref->{$req} <= 0;
    }
    return 1;
}
# The items %itemcount still holds go back to the player.
sub return_items {
    my $hashref = shift;
    foreach my $k (keys %{$hashref}) {
        main::emit('return_item', $k, $hashref->{$k}) if $k && $hashref->{$k} > 0;
    }
    return 1;
}
package main;

my $result = do $file;
if ($@) {
    emit('error', "$@");
    exit 0;
}
{
    no strict 'refs';
    &{"main::$event"}() if defined &{"main::$event"};
}
